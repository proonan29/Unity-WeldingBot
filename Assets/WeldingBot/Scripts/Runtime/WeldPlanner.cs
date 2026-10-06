using System.Collections.Generic;
using UnityEngine;

namespace WeldingBot
{
    [System.Serializable]
    public class GantryLimits
    {
        public Vector3 min = new Vector3(-20f, 1.6f, -7f);    // robot base (mount) position limits
        public Vector3 max = new Vector3(20f, 6.8f, 7f);
        public Vector3 speed = new Vector3(0.5f, 0.25f, 0.5f);// m/s per axis
        public float accel = 0.4f;                            // m/s^2
        public Vector3 home = new Vector3(-19f, 6.5f, 0f);
        public float clearance = 0.85f;                       // base must stay this far above nearby plates
        public float travelClearance = 2.3f;                  // parked robot height above plates while travelling

        public bool Inside(Vector3 p) =>
            p.x >= min.x - 1e-4f && p.x <= max.x + 1e-4f && p.y >= min.y - 1e-4f && p.y <= max.y + 1e-4f &&
            p.z >= min.z - 1e-4f && p.z <= max.z + 1e-4f;

        public static float AxisTime(float d, float v, float a)
        {
            d = Mathf.Abs(d);
            if (d < 1e-5f) return 0f;
            return d < v * v / a ? 2f * Mathf.Sqrt(d / a) : d / v + v / a;
        }
    }

    [System.Serializable]
    public class WeldParams
    {
        public float travelAngle = 10f;        // push angle (deg)
        public float approachDist = 0.10f;     // m, along the torch axis
        public float approachSpeed = 0.08f;    // m/s
        public float arcStart = 0.6f, crater = 0.6f;  // s
        public float speedFlat = 8f, speedHorizontal = 7f, speedVertical = 3.5f, speedOverhead = 4.5f; // mm/s
        public float buttFactor = 0.8f;
        public float sampleStep = 0.02f;       // m between IK samples along a seam

        public float Speed(Seam s)
        {
            float v = s.position switch
            {
                WeldPosition.Flat => speedFlat, WeldPosition.Horizontal => speedHorizontal,
                WeldPosition.Vertical => speedVertical, _ => speedOverhead
            };
            if (s.type == JointType.Butt) v *= buttFactor;
            return v * 0.001f;
        }
    }

    public enum WeldMode { Fixed, Track }

    /// <summary>How one seam is welded: travel direction, gantry station (fixed) or tracking offset, joint samples.</summary>
    public class SeamPlan
    {
        public int seam;
        public bool reversed;
        public WeldMode mode;
        public Vector3 base0, base1;      // gantry at weld start / end (equal for Fixed)
        public Vector3 p0, p1, axis;      // travel start/end and torch axis at the start
        public Vector3 travel, torchDir;
        public float ang0, ang1;          // travel angle at start / end (deg, + push, - drag)
        public Quaternion rot;
        public float length, speed;
        public float[] approachJ;
        public float[][] approachQs, weldQs, retractQs;
        public float cost;
        public Vector3 ApproachPoint(float d) => p0 - axis * d;
        public Vector3 AxisAt(float u) => (torchDir + travel * Mathf.Tan(Mathf.Lerp(ang0, ang1, u) * Mathf.Deg2Rad)).normalized;
        public Quaternion RotAt(float u) => Quaternion.LookRotation(AxisAt(u), travel);
    }

    public static class WeldPlanner
    {
        /// <summary>Seams up to this length are tried from a single gantry station first.</summary>
        public const float MaxFixedLength = 3.0f;
        public static readonly Quaternion BaseRotation = Quaternion.Euler(0f, 0f, 180f);
        static readonly Quaternion InvBase = Quaternion.Inverse(BaseRotation);

        public class Context
        {
            public RobotGeom geom;
            public GantryLimits lim;
            public WeldParams wp;
            public List<Bounds> obstacles = new List<Bounds>();
            public List<PlateSpec> plates = new List<PlateSpec>();
            public List<float> plateRadius = new List<float>();
            public float maxTop;
            public int collisionChecks, collisionHits;
            // background planning: progress and cancellation
            public volatile int progressDone, progressTotal;
            public volatile bool cancel;
            public float margin;   // extra clearance used while planning (verification uses 0)
            public void CheckCancel() { if (cancel) throw new System.OperationCanceledException(); }
            public readonly Vector3[] pts = new Vector3[6];
        }

        // link capsules: (from point, to point, radius); the torch stops short of the TCP (it touches the joint)
        static readonly int[] SegA = { 0, 1, 2, 3, 4 };
        static readonly int[] SegB = { 1, 2, 3, 4, 5 };
        static readonly float[] SegR = { 0.20f, 0.12f, 0.09f, 0.07f, 0.025f };
        const float TorchClear = 0.08f;

        [System.ThreadStatic] static List<float[]> solBufTS;
        [System.ThreadStatic] static List<float> solCostTS;
        static List<float[]> solBuf => solBufTS ??= new List<float[]>(8);
        static List<float> solCost => solCostTS ??= new List<float>(8);

        /// <summary>Most comfortable IK solution that does not collide with the plates (tries every arm configuration).</summary>
        public static bool SolveFree(Context c, Vector3 basePos, Vector3 world, Quaternion worldRot, float[] result, out float cost)
        {
            cost = float.MaxValue;
            RobotKinematics.SolveAll(c.geom, LocalPos(basePos, world), LocalRot(worldRot), null, solBuf);
            solCost.Clear();
            foreach (var q in solBuf)
                solCost.Add(RobotKinematics.PostureCost(c.geom, q) + 0.01f * (Mathf.Abs(q[1] + 60f) + Mathf.Abs(q[2] - 60f) + Mathf.Abs(q[4] + 90f) + Mathf.Abs(q[3])));
            for (int pass = 0; pass < solBuf.Count; pass++)
            {
                int best = -1;
                for (int i = 0; i < solBuf.Count; i++)
                    if (solCost[i] < float.MaxValue && (best < 0 || solCost[i] < solCost[best])) best = i;
                if (best < 0) break;
                if (!Collides(c, basePos, solBuf[best]))
                {
                    System.Array.Copy(solBuf[best], result, 6);
                    cost = solCost[best];
                    return true;
                }
                solCost[best] = float.MaxValue;
            }
            return false;
        }

        /// <summary>True if any robot link (as capsules) intersects a plate or jig.</summary>
        public static bool Collides(Context c, Vector3 basePos, float[] q)
        {
            c.collisionChecks++;
            var pts = c.pts;
            RobotKinematics.Points(c.geom, q, pts);
            for (int i = 0; i < 6; i++) pts[i] = basePos + BaseRotation * pts[i];
            for (int sgi = 0; sgi < SegA.Length; sgi++)
            {
                Vector3 a = pts[SegA[sgi]], b = pts[SegB[sgi]];
                float r = SegR[sgi] + (sgi == SegA.Length - 1 ? 0f : c.margin);   // the torch may come close to the joint
                if (sgi == SegA.Length - 1) b -= (b - a).normalized * TorchClear;
                float len = (b - a).magnitude;
                for (int k = 0; k < c.plates.Count; k++)
                {
                    var p = c.plates[k];
                    // bounding-sphere reject
                    Vector3 ab = b - a;
                    float t = len > 1e-5f ? Mathf.Clamp01(Vector3.Dot(p.center - a, ab) / (len * len)) : 0f;
                    if ((a + ab * t - p.center).sqrMagnitude > (c.plateRadius[k] + r) * (c.plateRadius[k] + r)) continue;
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.04f));
                    for (int j = 0; j <= n; j++)
                    {
                        var l = p.ToLocal(Vector3.Lerp(a, b, j / (float)n));
                        if (Mathf.Abs(l.x) < p.HX + r && Mathf.Abs(l.y) < p.HT + r && Mathf.Abs(l.z) < p.HZ + r)
                        { c.collisionHits++; return true; }
                    }
                }
            }
            return false;
        }

        public static Vector3 LocalPos(Vector3 basePos, Vector3 world) => InvBase * (world - basePos);
        public static Quaternion LocalRot(Quaternion world) => InvBase * world;

        /// <summary>Minimum base height above plates near (x, z).</summary>
        public static float ClearY(Context c, Vector3 b)
        {
            float top = 0f;
            foreach (var o in c.obstacles)
            {
                if (b.x < o.min.x - 1.2f || b.x > o.max.x + 1.2f || b.z < o.min.z - 1.2f || b.z > o.max.z + 1.2f) continue;
                top = Mathf.Max(top, o.max.y);
            }
            return top + c.lim.clearance;
        }

        static bool BaseOk(Context c, Vector3 b) => c.lim.Inside(b) && b.y >= ClearY(c, b);

        /// <summary>Torch travel-angle profiles (start, end) in degrees; + = push, - = drag.
        /// Varying profiles tilt the torch away from walls at the seam ends.</summary>
        public static readonly Vector2[] AngleProfiles =
        {
            new Vector2(10f, 10f), new Vector2(-25f, 25f), new Vector2(-25f, 10f), new Vector2(10f, 25f),
            new Vector2(-10f, -10f), new Vector2(0f, 0f)
        };

        static void Frame(Seam s, bool rev, WeldParams wp, SeamPlan sp, Vector2 angles)
        {
            sp.seam = s.id;
            sp.reversed = rev;
            sp.p0 = rev ? s.p1 : s.p0;
            sp.p1 = rev ? s.p0 : s.p1;
            sp.length = s.Length;
            sp.ang0 = angles.x; sp.ang1 = angles.y;
            Vector3 t = (sp.p1 - sp.p0) / Mathf.Max(sp.length, 1e-6f);
            sp.travel = t;
            sp.torchDir = s.torchDir;
            sp.axis = sp.AxisAt(0f);
            sp.rot = sp.RotAt(0f);
            sp.speed = wp.Speed(s);
        }

        static IEnumerable<Vector3> Offsets()
        {
            float[] dys = { 1.1f, 1.3f, 1.5f, 1.7f, 1.9f, 2.1f, 2.4f };
            float[] rs = { 0.25f, 0.5f, 0.75f, 1.0f, 1.3f };
            foreach (var dy in dys)
            {
                yield return new Vector3(0f, dy, 0f);
                foreach (var r in rs)
                for (int k = 0; k < 12; k++)
                {
                    float a = k * 30f * Mathf.Deg2Rad;
                    yield return new Vector3(r * Mathf.Cos(a), dy, r * Mathf.Sin(a));
                }
            }
        }

        static float[][] Path(Context c, Vector3 b0, Vector3 b1, Vector3 pa, Vector3 pb, Quaternion ra, Quaternion rb, float step,
            float[] seed, ref float cost, out bool ok)
        {
            ok = true;
            float L = (pb - pa).magnitude;
            int n = Mathf.Max(2, Mathf.CeilToInt(L / step) + 1);
            var qs = new float[n][];
            float[] prev = seed;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                Vector3 b = Vector3.Lerp(b0, b1, u);
                var q = new float[6];
                if (!RobotKinematics.Solve(c.geom, LocalPos(b, Vector3.Lerp(pa, pb, u)), LocalRot(Quaternion.Slerp(ra, rb, u)), prev, prev != null, q, out float pc))
                { ok = false; return null; }
                if (prev != null && RobotKinematics.MaxDelta(prev, q) > 25f) { ok = false; return null; }
                if ((i % 5 == 0 || i == n - 1) && Collides(c, b, q)) { ok = false; return null; }
                cost += pc;
                qs[i] = q;
                prev = q;
            }
            return qs;
        }

        /// <summary>Full feasibility check of a seam plan with the given gantry start/end, filling joint samples.</summary>
        static bool Build(Context c, SeamPlan sp, Vector3 b0, Vector3 b1, float[] seed)
        {
            var wp = c.wp;
            float cost = 0f;
            Quaternion r0 = sp.RotAt(0f), r1 = sp.RotAt(1f);
            Vector3 appr = sp.p0 - sp.AxisAt(0f) * wp.approachDist;
            var qa = new float[6];
            float pc;
            if (seed != null)
            {
                if (!RobotKinematics.Solve(c.geom, LocalPos(b0, appr), LocalRot(r0), seed, true, qa, out pc) || Collides(c, b0, qa))
                    if (!SolveFree(c, b0, appr, r0, qa, out pc)) return false;
            }
            else if (!SolveFree(c, b0, appr, r0, qa, out pc)) return false;
            cost += pc;
            sp.approachQs = Path(c, b0, b0, appr, sp.p0, r0, r0, 0.02f, qa, ref cost, out bool ok);
            if (!ok) return false;
            sp.weldQs = Path(c, b0, b1, sp.p0, sp.p1, r0, r1, wp.sampleStep, sp.approachQs[sp.approachQs.Length - 1], ref cost, out ok);
            if (!ok) return false;
            Vector3 ret = sp.p1 - sp.AxisAt(1f) * wp.approachDist;
            sp.retractQs = Path(c, b1, b1, sp.p1, ret, r1, r1, 0.02f, sp.weldQs[sp.weldQs.Length - 1], ref cost, out ok);
            if (!ok) return false;
            sp.approachJ = qa;
            sp.base0 = b0; sp.base1 = b1;
            sp.cost = cost / (sp.approachQs.Length + sp.weldQs.Length + sp.retractQs.Length);
            return true;
        }

        /// <summary>Try welding the seam without moving the gantry from <paramref name="b"/>.</summary>
        public static SeamPlan TryFixed(Context c, Seam s, bool rev, Vector3 b, float[] seed, Vector2 angles)
        {
            if (!BaseOk(c, b)) return null;
            var sp = new SeamPlan { mode = WeldMode.Fixed };
            Frame(s, rev, c.wp, sp, angles);
            if ((sp.p0 - b).magnitude > c.geom.Reach + 1.0f || (sp.p1 - b).magnitude > c.geom.Reach + 1.0f) return null;
            return Build(c, sp, b, b, seed) ? sp : null;
        }

        /// <summary>Best plan for a seam in one direction: fixed station if possible, otherwise gantry tracking.
        /// Torch-angle profiles are tried in order until one works.</summary>
        public static SeamPlan BestPlan(Context c, Seam s, bool rev)
        {
            foreach (var prof in AngleProfiles)
            {
                c.CheckCancel();
                var sp = BestPlan(c, s, rev, prof);
                if (sp != null) return sp;
            }
            return null;
        }

        static SeamPlan BestPlan(Context c, Seam s, bool rev, Vector2 angles)
        {
            var proto = new SeamPlan();
            Frame(s, rev, c.wp, proto, angles);
            var q = new float[6];
            Quaternion rMid = proto.RotAt(0.5f);

            // 1) Fixed station: rank candidates by posture at the seam midpoint, then verify the full path.
            if (s.Length <= MaxFixedLength)
            {
                var ranked = new List<(float cost, Vector3 b)>();
                Vector3 mid = s.Mid;
                foreach (var o in Offsets())
                {
                    var b = mid + o;
                    if (!BaseOk(c, b)) continue;
                    if (!SolveFree(c, b, mid, rMid, q, out float pc)) continue;
                    ranked.Add((pc, b));
                }
                ranked.Sort((x, y) => x.cost.CompareTo(y.cost));
                SeamPlan best = null;
                int found = 0;
                for (int i = 0; i < ranked.Count && i < 60 && found < 3; i++)
                {
                    var sp = TryFixed(c, s, rev, ranked[i].b, null, angles);
                    if (sp == null) continue;
                    found++;
                    if (best == null || sp.cost < best.cost) best = sp;
                }
                if (best != null) return best;
            }

            // 2) Tracking: constant offset between TCP and gantry while welding.
            {
                var ranked = new List<(float cost, Vector3 o)>();
                foreach (var o in Offsets())
                {
                    Vector3 b0 = proto.p0 + o, b1 = proto.p1 + o;
                    if (!BaseOk(c, b0) || !BaseOk(c, b1)) continue;
                    if (!SolveFree(c, b0, proto.p0, proto.RotAt(0f), q, out float pc)) continue;
                    ranked.Add((pc, o));
                }
                ranked.Sort((x, y) => x.cost.CompareTo(y.cost));
                SeamPlan best = null;
                int found = 0;
                for (int i = 0; i < ranked.Count && i < 40 && found < 3; i++)
                {
                    var sp = new SeamPlan { mode = WeldMode.Track };
                    Frame(s, rev, c.wp, sp, angles);
                    if (!Build(c, sp, sp.p0 + ranked[i].o, sp.p1 + ranked[i].o, null)) continue;
                    found++;
                    if (best == null || sp.cost < best.cost) best = sp;
                }
                return best;
            }
        }

        public static float AirTime(RobotGeom g, float[] a, float[] b)
        {
            float t = 0f;
            for (int i = 0; i < 6; i++) t = Mathf.Max(t, Mathf.Abs(a[i] - b[i]) / g.vMax[i]);
            return t < 1e-3f ? 0f : Mathf.Max(0.4f, t) + 0.2f;
        }

        public static GantryMotion GantryMove(Context c, Vector3 a, Vector3 b, float[] parkQ)
        {
            var lim = c.lim;
            float safe = Mathf.Clamp(Mathf.Max(Mathf.Max(a.y, b.y), c.maxTop + lim.travelClearance), lim.min.y, lim.max.y);
            var m = new GantryMotion { kind = MotionKind.GantryMove, a = a, b = b, safeY = safe, q = (float[])parkQ.Clone() };
            m.tUp = GantryLimits.AxisTime(safe - a.y, lim.speed.y, lim.accel);
            m.tXZ = Mathf.Max(GantryLimits.AxisTime(b.x - a.x, lim.speed.x, lim.accel), GantryLimits.AxisTime(b.z - a.z, lim.speed.z, lim.accel));
            m.tDown = GantryLimits.AxisTime(safe - b.y, lim.speed.y, lim.accel);
            m.dur = m.tUp + m.tXZ + m.tDown;
            return m;
        }

        public static float GantryDistance(GantryMotion m) =>
            Mathf.Abs(m.safeY - m.a.y) + Mathf.Abs(m.safeY - m.b.y) + Mathf.Abs(m.b.x - m.a.x) + Mathf.Abs(m.b.z - m.a.z);
    }
}
