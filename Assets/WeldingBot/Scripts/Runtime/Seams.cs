using System.Collections.Generic;
using UnityEngine;

namespace WeldingBot
{
    public enum JointType { Fillet, Butt }
    public enum WeldPosition { Flat, Horizontal, Vertical, Overhead }

    /// <summary>A straight weld seam found between two plates.</summary>
    public class Seam
    {
        public int id;
        public string name;
        public JointType type;
        public WeldPosition position;
        public int plateA, plateB;
        public Vector3 p0, p1;        // root line (where the bead sits)
        public Vector3 torchDir;      // unit, pointing from the torch INTO the joint
        public Vector3 u1, u2;        // unit directions along the two surfaces, away from the root (bead profile)
        public float leg;             // fillet leg / butt bead half-width (m)
        public float angleDeg;        // included angle between the two surfaces at the joint

        public float Length => (p1 - p0).magnitude;
        public Vector3 Dir => (p1 - p0).normalized;
        public Vector3 Mid => (p0 + p1) * 0.5f;

        public string PositionCode => (type == JointType.Fillet ? "" : "") + (position switch
        {
            WeldPosition.Flat => "1", WeldPosition.Horizontal => "2", WeldPosition.Vertical => "3", _ => "4"
        }) + (type == JointType.Fillet ? "F" : "G");

        /// <summary>Deposited cross-section (m^2) used for the wire estimate.</summary>
        public float DepositArea => type == JointType.Fillet ? 0.5f * leg * leg : leg * leg * 1.2f;
    }

    public static class SeamExtractor
    {
        public static List<Seam> Extract(IList<PlateSpec> plates)
        {
            var list = new List<Seam>();
            for (int a = 0; a < plates.Count; a++)
            for (int b = 0; b < plates.Count; b++)
            {
                if (a == b || !plates[a].weldable || !plates[b].weldable) continue;
                FilletEdgeOnFace(plates, a, b, list);
            }
            for (int a = 0; a < plates.Count; a++)
            for (int b = a + 1; b < plates.Count; b++)
            {
                if (!plates[a].weldable || !plates[b].weldable) continue;
                ButtEdges(plates, a, b, list);
            }
            list = SplitAtObstructions(plates, list);
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                s.id = i;
                s.name = $"S{i + 1:00}";
                s.position = Classify(s);
            }
            return list;
        }

        /// <summary>
        /// Cut seams where another member passes through the joint (e.g. a bulkhead crossing a wall fillet),
        /// leaving the free pieces (trimmed 1 cm from the obstruction).
        /// </summary>
        static List<Seam> SplitAtObstructions(IList<PlateSpec> plates, List<Seam> seams)
        {
            var result = new List<Seam>();
            const float step = 0.01f, trim = 0.01f, minLen = 0.05f;
            foreach (var s in seams)
            {
                float L = s.Length;
                int n = Mathf.Max(2, Mathf.CeilToInt(L / step) + 1);
                var blocked = new bool[n];
                Vector3 probeOff = -s.torchDir * 0.004f;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = Vector3.Lerp(s.p0, s.p1, i / (float)(n - 1)) + probeOff;
                    for (int k = 0; k < plates.Count; k++)
                    {
                        if (k == s.plateA || k == s.plateB) continue;
                        var l = plates[k].ToLocal(p);
                        if (Mathf.Abs(l.x) < plates[k].HX && Mathf.Abs(l.y) < plates[k].HT + 0.002f && Mathf.Abs(l.z) < plates[k].HZ)
                        { blocked[i] = true; break; }
                    }
                }
                int start = -1;
                for (int i = 0; i <= n; i++)
                {
                    bool free = i < n && !blocked[i];
                    if (free && start < 0) start = i;
                    if (!free && start >= 0)
                    {
                        float a = start / (float)(n - 1) * L, b = (i - 1) / (float)(n - 1) * L;
                        if (start > 0) a += trim;
                        if (i < n) b -= trim;
                        if (b - a >= minLen)
                        {
                            Vector3 d = (s.p1 - s.p0) / L;
                            result.Add(new Seam
                            {
                                type = s.type, plateA = s.plateA, plateB = s.plateB, p0 = s.p0 + d * a, p1 = s.p0 + d * b,
                                torchDir = s.torchDir, u1 = s.u1, u2 = s.u2, leg = s.leg, angleDeg = s.angleDeg
                            });
                        }
                        start = -1;
                    }
                }
            }
            return result;
        }

        public static WeldPosition Classify(Seam s)
        {
            Vector3 t = s.Dir;
            if (Mathf.Abs(t.y) > 0.6f) return WeldPosition.Vertical;
            if (s.torchDir.y < -0.85f) return WeldPosition.Flat;
            if (s.torchDir.y > 0.35f) return WeldPosition.Overhead;
            return WeldPosition.Horizontal;
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-9f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }

        /// <summary>Liang-Barsky clip of segment pa-pb (on B's face) to B's rectangle, inset by <paramref name="inset"/>.</summary>
        static bool ClipToRect(PlateSpec B, Vector3 pa, Vector3 pb, float inset, out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            Vector3 la = B.ToLocal(pa), lb = B.ToLocal(pb), d = lb - la;
            float hx = B.HX - inset, hz = B.HZ - inset;
            return Clip(-d.x, la.x + hx, ref t0, ref t1) && Clip(d.x, hx - la.x, ref t0, ref t1) &&
                   Clip(-d.z, la.z + hz, ref t0, ref t1) && Clip(d.z, hz - la.z, ref t0, ref t1) && t1 - t0 > 1e-4f;
        }

        /// <summary>T-joint: an edge of A rests on a face of B → one fillet seam on each side of A.</summary>
        static void FilletEdgeOnFace(IList<PlateSpec> plates, int ia, int ib, List<Seam> list)
        {
            var A = plates[ia]; var B = plates[ib];
            foreach (var e in A.Edges())
            {
                for (int s = 1; s >= -1; s -= 2)
                {
                    Vector3 nB = B.Normal * s;
                    Vector3 faceP = B.center + nB * B.HT;
                    float da = Vector3.Dot(e.a - faceP, nB), db = Vector3.Dot(e.b - faceP, nB);
                    float tol = A.HT + 0.004f;
                    if (da < -0.004f || db < -0.004f || da > tol || db > tol) continue;
                    if (Vector3.Dot(A.center - faceP, nB) <= 0.01f) continue;
                    if (Vector3.Dot(e.outward, nB) > -0.25f) continue;
                    Vector3 pa = e.a - nB * da, pb = e.b - nB * db;
                    if (!ClipToRect(B, pa, pb, 0.005f, out float t0, out float t1)) continue;
                    Vector3 c0 = Vector3.Lerp(pa, pb, t0), c1 = Vector3.Lerp(pa, pb, t1);
                    if ((c1 - c0).magnitude < 0.05f) continue;

                    for (int sg = 1; sg >= -1; sg -= 2)
                    {
                        Vector3 nA = A.Normal * sg;
                        Vector3 u = nA - Vector3.Dot(nA, nB) * nB;
                        if (u.sqrMagnitude < 1e-6f) continue;
                        u.Normalize();
                        float du = Vector3.Dot(u, nA);
                        if (du < 0.05f) continue;
                        Vector3 q0 = c0 + u * ((A.HT - Vector3.Dot(c0 - A.center, nA)) / du);
                        Vector3 q1 = c1 + u * ((A.HT - Vector3.Dot(c1 - A.center, nA)) / du);
                        if (!B.InRect(q0, 0.002f) || !B.InRect(q1, 0.002f)) continue;
                        float included = 180f - Vector3.Angle(nA, nB);
                        if (included < 30f) continue;   // too acute for the torch
                        list.Add(new Seam
                        {
                            type = JointType.Fillet, plateA = ia, plateB = ib, p0 = q0, p1 = q1,
                            torchDir = -(nA + nB).normalized, u1 = u, u2 = -e.outward,
                            leg = Mathf.Clamp(0.7f * Mathf.Min(A.thickness, B.thickness), 0.004f, 0.008f),
                            angleDeg = included
                        });
                    }
                }
            }
        }

        static float SideSign(PlateSpec p, PlateSpec other, PlateEdge e)
        {
            if (Mathf.Abs(p.Normal.y) > 0.2f) return Mathf.Sign(p.Normal.y);
            float d = Vector3.Dot(p.Normal, e.Mid - other.center);
            return d >= 0f ? 1f : -1f;
        }

        static float DistToLine(Vector3 p, Vector3 o, Vector3 dir)
        {
            Vector3 v = p - o;
            return (v - Vector3.Dot(v, dir) * dir).magnitude;
        }

        /// <summary>Edge-to-edge joint (coplanar butt or angled corner/ridge/valley).</summary>
        static void ButtEdges(IList<PlateSpec> plates, int ia, int ib, List<Seam> list)
        {
            var A = plates[ia]; var B = plates[ib];
            foreach (var ea in A.Edges())
            foreach (var eb in B.Edges())
            {
                Vector3 da = ea.b - ea.a; float La = da.magnitude; da /= La;
                Vector3 db = (eb.b - eb.a).normalized;
                if (Mathf.Abs(Vector3.Dot(da, db)) < 0.985f) continue;
                if (Vector3.Dot(ea.outward, eb.outward) > 0.2f) continue;
                float tol = Mathf.Max(A.HT, B.HT) * 1.5f + 0.004f;
                if (DistToLine(eb.a, ea.a, da) > tol || DistToLine(eb.b, ea.a, da) > tol) continue;
                float s0 = Vector3.Dot(eb.a - ea.a, da), s1 = Vector3.Dot(eb.b - ea.a, da);
                if (s0 > s1) { var tmp = s0; s0 = s1; s1 = tmp; }
                float lo = Mathf.Max(0f, s0) + 0.005f, hi = Mathf.Min(La, s1) - 0.005f;
                if (hi - lo < 0.05f) continue;
                float sa = SideSign(A, B, ea), sb = SideSign(B, A, eb);
                Vector3 nA = A.Normal * sa, nB = B.Normal * sb;
                Vector3 sum = nA + nB;
                if (sum.sqrMagnitude < 0.05f) continue;
                Vector3 m0 = ea.a + da * lo, m1 = ea.a + da * hi;
                Vector3 mb0 = eb.a + db * Vector3.Dot(m0 - eb.a, db), mb1 = eb.a + db * Vector3.Dot(m1 - eb.a, db);
                Vector3 p0 = ((m0 + nA * A.HT) + (mb0 + nB * B.HT)) * 0.5f;
                Vector3 p1 = ((m1 + nA * A.HT) + (mb1 + nB * B.HT)) * 0.5f;
                list.Add(new Seam
                {
                    type = JointType.Butt, plateA = ia, plateB = ib, p0 = p0, p1 = p1,
                    torchDir = -sum.normalized, u1 = -ea.outward, u2 = -eb.outward,
                    leg = Mathf.Clamp(0.6f * Mathf.Min(A.thickness, B.thickness), 0.005f, 0.010f),
                    angleDeg = 180f - Vector3.Angle(nA, nB)
                });
            }
        }
    }
}
