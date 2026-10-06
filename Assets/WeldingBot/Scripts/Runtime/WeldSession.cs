using System.Collections.Generic;
using UnityEngine;

namespace WeldingBot
{
    /// <summary>Plan-wide and live statistics.</summary>
    public class WeldStats
    {
        public float totalTime, arcTime, weldTime, gantryTime, airTime, approachTime;
        public float seamLength, plannedLength, weldedLength, gantryDistance, wireKg;
        public int seams, seamsPlanned, seamsDone, stations, gantryMoves, trackedSeams;
        public readonly float[] lengthByPosition = new float[4];
        public readonly float[] lengthByType = new float[2];
        public readonly List<int> unreachable = new List<int>();
        public float ArcRatio => totalTime > 0f ? arcTime / totalTime : 0f;
        public float planningMs;
        public int unsafeAirMoves;      // air moves for which no collision-free route was found
    }

    /// <summary>Everything about one job: plates → seams → plan → deterministic timeline.</summary>
    public class WeldSession
    {
        public WeldJob job;
        public List<Seam> seams;
        public readonly List<SeamPlan> order = new List<SeamPlan>();
        public readonly Dictionary<int, SeamPlan> planOf = new Dictionary<int, SeamPlan>();
        public readonly Dictionary<int, SampledMotion> weldMotionOf = new Dictionary<int, SampledMotion>();
        public Timeline timeline = new Timeline();
        public WeldStats stats = new WeldStats();
        public WeldPlanner.Context ctx;

        public static WeldSession Build(WeldJob job, RobotGeom geom, GantryLimits lim, WeldParams wp)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var s = new WeldSession { job = job, seams = SeamExtractor.Extract(job.plates) };
            var c = s.ctx = new WeldPlanner.Context { geom = geom, lim = lim, wp = wp };
            foreach (var p in job.plates)
            {
                var b = p.WorldBounds();
                c.obstacles.Add(b);
                c.plates.Add(p);
                c.plateRadius.Add(Mathf.Sqrt(p.HX * p.HX + p.HZ * p.HZ + p.HT * p.HT));
                c.maxTop = Mathf.Max(c.maxTop, b.max.y);
            }

            // candidate plans per seam and direction (vertical seams are welded uphill only)
            var options = new Dictionary<int, List<SeamPlan>>();
            foreach (var seam in s.seams)
            {
                var list = new List<SeamPlan>();
                bool vertical = seam.position == WeldPosition.Vertical;
                bool upIsForward = seam.p1.y >= seam.p0.y;
                for (int d = 0; d < 2; d++)
                {
                    bool rev = d == 1;
                    if (vertical && rev == upIsForward) continue;
                    var sp = WeldPlanner.BestPlan(c, seam, rev);
                    if (sp != null) list.Add(sp);
                }
                if (list.Count > 0) options[seam.id] = list;
                else s.stats.unreachable.Add(seam.id);
            }

            // greedy ordering: cheapest transition next, reusing the current gantry station when possible
            var park = (float[])RobotGeom.Park.Clone();
            Vector3 curB = lim.home;
            float[] curJ = park;
            bool atHome = true;
            var remaining = new List<int>(options.Keys);
            while (remaining.Count > 0)
            {
                SeamPlan best = null; float bestCost = float.MaxValue; bool bestReuse = false;
                foreach (var id in remaining)
                {
                    var seam = s.seams[id];
                    foreach (var opt in options[id])
                    {
                        // option A: weld from the current station
                        if (!atHome && seam.Length <= WeldPlanner.MaxFixedLength && (seam.Mid - curB).magnitude < geom.Reach + 1.2f)
                        {
                            var reuse = WeldPlanner.TryFixed(c, seam, opt.reversed, curB, curJ, new Vector2(opt.ang0, opt.ang1));
                            if (reuse != null)
                            {
                                float cr = WeldPlanner.AirTime(geom, curJ, reuse.approachJ);
                                if (cr < bestCost) { bestCost = cr; best = reuse; bestReuse = true; }
                            }
                        }
                        // option B: move the gantry to the seam's own best station
                        float cm = WeldPlanner.AirTime(geom, curJ, park) +
                                   WeldPlanner.GantryMove(c, curB, opt.base0, park).dur +
                                   WeldPlanner.AirTime(geom, park, opt.approachJ) + 3f;   // 3 s bias favours reuse
                        if (cm < bestCost) { bestCost = cm; best = opt; bestReuse = false; }
                    }
                }
                s.Append(best, bestReuse, ref curB, ref curJ, park);
                atHome = false;
                remaining.Remove(best.seam);
            }
            // back home
            if (!atHome)
            {
                s.AddAirSafe(curB, curJ, park, MotionKind.Park, -1);
                s.AddGantry(curB, lim.home, park);
            }
            else s.timeline.Add(new Dwell { kind = MotionKind.Idle, basePos = lim.home, q = park, dur = 0.01f });

            s.ComputeStats();
            s.stats.planningMs = (float)sw.Elapsed.TotalMilliseconds;
            return s;
        }

        void AddAir(Vector3 b, float[] qa, float[] qb, MotionKind kind, int seam)
        {
            float t = WeldPlanner.AirTime(ctx.geom, qa, qb);
            if (t <= 0f) return;
            timeline.Add(new JointMove { kind = kind, basePos = b, qa = (float[])qa.Clone(), qb = (float[])qb.Clone(), dur = t, seam = seam });
        }

        bool JointPathCollides(Vector3 b, float[] qa, float[] qb)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(RobotKinematics.MaxDelta(qa, qb, 6) / 4f) + 1);
            var q = new float[6];
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                u = u * u * (3f - 2f * u);
                for (int k = 0; k < 6; k++) q[k] = Mathf.Lerp(qa[k], qb[k], u);
                if (WeldPlanner.Collides(ctx, b, q)) return true;
            }
            return false;
        }

        /// <summary>TCP lifted straight up by h (same orientation), solved close to q.</summary>
        float[] Lifted(Vector3 b, float[] q, float h)
        {
            RobotKinematics.FK(ctx.geom, q, out var lp, out var lr, out _);
            Vector3 wp = b + WeldPlanner.BaseRotation * lp + Vector3.up * h;
            var r = new float[6];
            if (!RobotKinematics.Solve(ctx.geom, WeldPlanner.LocalPos(b, wp), lr, q, true, r, out _)) return null;
            return RobotKinematics.MaxDelta(q, r, 6) > 120f ? null : r;
        }

        /// <summary>Air move that avoids the plates: direct, via the folded park pose, or via lifted tool poses.</summary>
        void AddAirSafe(Vector3 b, float[] qa, float[] qb, MotionKind kind, int seam)
        {
            if (!JointPathCollides(b, qa, qb)) { AddAir(b, qa, qb, kind, seam); return; }
            var park = RobotGeom.Park;
            foreach (var h in new[] { 0.25f, 0.5f, 0.8f, 1.1f, 1.4f })
            {
                var la = Lifted(b, qa, h);
                if (la == null || JointPathCollides(b, qa, la)) continue;
                var lb = Lifted(b, qb, h);
                if (lb != null && !JointPathCollides(b, la, lb) && !JointPathCollides(b, lb, qb))
                {
                    AddAir(b, qa, la, kind, seam); AddAir(b, la, lb, kind, seam); AddAir(b, lb, qb, kind, seam);
                    return;
                }
                if (!JointPathCollides(b, la, park))
                {
                    if (!JointPathCollides(b, park, qb)) { AddAir(b, qa, la, kind, seam); AddAir(b, la, park, kind, seam); AddAir(b, park, qb, kind, seam); return; }
                    var lb2 = Lifted(b, qb, h);
                    if (lb2 != null && !JointPathCollides(b, park, lb2) && !JointPathCollides(b, lb2, qb))
                    { AddAir(b, qa, la, kind, seam); AddAir(b, la, park, kind, seam); AddAir(b, park, lb2, kind, seam); AddAir(b, lb2, qb, kind, seam); return; }
                }
            }
            if (!JointPathCollides(b, qa, park) && !JointPathCollides(b, park, qb))
            { AddAir(b, qa, park, kind, seam); AddAir(b, park, qb, kind, seam); return; }
            stats.unsafeAirMoves++;
            AddAir(b, qa, qb, kind, seam);
        }

        void AddGantry(Vector3 a, Vector3 b, float[] park)
        {
            if ((a - b).magnitude < 1e-3f) return;
            var m = WeldPlanner.GantryMove(ctx, a, b, park);
            timeline.Add(m);
        }

        void Append(SeamPlan sp, bool reuse, ref Vector3 curB, ref float[] curJ, float[] park)
        {
            var wp = ctx.wp;
            int id = sp.seam;
            if (!reuse)
            {
                AddAirSafe(curB, curJ, park, MotionKind.Park, id);
                AddGantry(curB, sp.base0, park);
                AddAirSafe(sp.base0, park, sp.approachJ, MotionKind.AirMove, id);
            }
            else AddAirSafe(curB, curJ, sp.approachJ, MotionKind.AirMove, id);

            timeline.Add(new SampledMotion { kind = MotionKind.Approach, seam = id, base0 = sp.base0, base1 = sp.base0, qs = sp.approachQs, dur = wp.approachDist / wp.approachSpeed });
            var startQ = sp.weldQs[0];
            timeline.Add(new Dwell { kind = MotionKind.ArcStart, seam = id, basePos = sp.base0, q = startQ, arc = true, dur = wp.arcStart });
            var weld = new SampledMotion { kind = MotionKind.Weld, seam = id, base0 = sp.base0, base1 = sp.base1, qs = sp.weldQs, arc = true, length = sp.length, dur = sp.length / sp.speed };
            timeline.Add(weld);
            weldMotionOf[id] = weld;
            var endQ = sp.weldQs[sp.weldQs.Length - 1];
            timeline.Add(new Dwell { kind = MotionKind.Crater, seam = id, basePos = sp.base1, q = endQ, arc = true, progress = sp.length, dur = wp.crater });
            timeline.Add(new SampledMotion { kind = MotionKind.Retract, seam = id, base0 = sp.base1, base1 = sp.base1, qs = sp.retractQs, dur = wp.approachDist / wp.approachSpeed });
            curB = sp.base1;
            curJ = sp.retractQs[sp.retractQs.Length - 1];
            order.Add(sp);
            planOf[id] = sp;
        }

        void ComputeStats()
        {
            var st = stats;
            st.seams = seams.Count;
            foreach (var s in seams) st.seamLength += s.Length;
            st.totalTime = timeline.Duration;
            var stations = new List<Vector3>();
            foreach (var m in timeline.motions)
            {
                switch (m.kind)
                {
                    case MotionKind.GantryMove:
                        st.gantryTime += m.dur; st.gantryMoves++;
                        st.gantryDistance += WeldPlanner.GantryDistance((GantryMotion)m);
                        break;
                    case MotionKind.AirMove: case MotionKind.Park: st.airTime += m.dur; break;
                    case MotionKind.Approach: case MotionKind.Retract: st.approachTime += m.dur; break;
                    case MotionKind.ArcStart: case MotionKind.Crater: st.arcTime += m.dur; break;
                    case MotionKind.Weld:
                        st.arcTime += m.dur; st.weldTime += m.dur;
                        break;
                }
            }
            foreach (var sp in order)
            {
                var s = seams[sp.seam];
                st.seamsPlanned++;
                st.plannedLength += s.Length;
                st.lengthByPosition[(int)s.position] += s.Length;
                st.lengthByType[(int)s.type] += s.Length;
                st.wireKg += s.DepositArea * s.Length * 7850f / 0.92f;
                if (sp.mode == WeldMode.Track) st.trackedSeams++;
                bool known = false;
                foreach (var b in stations) if ((b - sp.base0).sqrMagnitude < 1e-4f) { known = true; break; }
                if (!known && sp.mode == WeldMode.Fixed) stations.Add(sp.base0);
            }
            st.stations = stations.Count;
        }

        /// <summary>Live statistics at simulated time t.</summary>
        public WeldStats StatsAt(float t)
        {
            var r = new WeldStats { totalTime = Mathf.Min(t, timeline.Duration), seams = stats.seams, seamLength = stats.seamLength, plannedLength = stats.plannedLength, seamsPlanned = stats.seamsPlanned };
            r.unreachable.AddRange(stats.unreachable);
            foreach (var m in timeline.motions)
            {
                if (m.t0 >= t) break;
                float d = Mathf.Min(t, m.T1) - m.t0;
                switch (m.kind)
                {
                    case MotionKind.GantryMove: r.gantryTime += d; break;
                    case MotionKind.AirMove: case MotionKind.Park: r.airTime += d; break;
                    case MotionKind.Approach: case MotionKind.Retract: r.approachTime += d; break;
                    case MotionKind.ArcStart: r.arcTime += d; break;
                    case MotionKind.Crater: r.arcTime += d; if (t >= m.T1) r.seamsDone++; break;
                    case MotionKind.Weld:
                        r.arcTime += d; r.weldTime += d;
                        var w = (SampledMotion)m;
                        float len = w.length * Mathf.Clamp01(d / m.dur);
                        r.weldedLength += len;
                        var s = seams[m.seam];
                        r.wireKg += s.DepositArea * len * 7850f / 0.92f;
                        r.lengthByPosition[(int)s.position] += len;
                        r.lengthByType[(int)s.type] += len;
                        break;
                }
            }
            r.gantryMoves = stats.gantryMoves; r.stations = stats.stations; r.trackedSeams = stats.trackedSeams;
            return r;
        }

        /// <summary>Simulated time at which a point at distance sOrig (along p0→p1 of the seam) is deposited; +inf if never.</summary>
        public float DepositTime(int seam, float sOrig)
        {
            if (!weldMotionOf.TryGetValue(seam, out var m)) return 1e9f;
            var sp = planOf[seam];
            float s = sp.reversed ? sp.length - sOrig : sOrig;
            return m.t0 + Mathf.Clamp(s, 0f, sp.length) / sp.speed;
        }
    }
}
