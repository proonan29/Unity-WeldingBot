using System.Collections.Generic;
using UnityEngine;

namespace WeldingBot
{
    /// <summary>
    /// 6-axis industrial robot with a spherical wrist (KUKA/ABB-like), expressed in the robot base frame
    /// using Unity axes (+Y = robot "up", +Z = forward at q1 = 0).
    /// Joint axes: J1 up, J2 right, J3 right, J4 forward (forearm roll), J5 right (bend), J6 forward (flange).
    /// The robot is mounted upside down under the gantry, so robot "up" points at the floor.
    /// </summary>
    [System.Serializable]
    public class RobotGeom
    {
        public float d1 = 0.50f;        // base -> shoulder height
        public float a1 = 0.20f;        // shoulder forward offset
        public float L2 = 0.85f;        // upper arm
        public float L3 = 0.90f;        // elbow -> wrist centre
        public float wristSplit = 0.30f;// J4 location along the forearm (visual only)
        public float d6 = 0.12f;        // wrist centre -> flange
        public float tool = 0.38f;      // flange -> TCP (straight torch)
        public float[] qMin = { -185f, -150f, -160f, -350f, -125f, -350f };
        public float[] qMax = { 185f, 40f, 160f, 350f, 125f, 350f };
        public float[] vMax = { 90f, 80f, 90f, 180f, 180f, 240f };   // deg/s for air moves

        public static readonly float[] Home = { 0f, -60f, 60f, 0f, -90f, 0f };
        /// <summary>Folded pose (arm raised toward the gantry) used while the gantry travels.</summary>
        public static readonly float[] Park = { 0f, 30f, 65f, 0f, 20f, 0f };
        public float Reach => L2 + L3;
    }

    public static class RobotKinematics
    {
        static readonly Vector3[] Axes = { Vector3.up, Vector3.right, Vector3.right, Vector3.forward, Vector3.right, Vector3.forward };
        public static Vector3 Axis(int i) => Axes[i];
        static readonly float[] ContW = { 1f, 1f, 1f, 0.6f, 0.6f, 0.3f };
        [System.ThreadStatic] static List<float[]> bufTS;
        static List<float[]> buf => bufTS ??= new List<float[]>(8);

        public static void FK(RobotGeom g, float[] q, out Vector3 pos, out Quaternion rot, out Vector3 wrist)
        {
            Quaternion r1 = Quaternion.AngleAxis(q[0], Vector3.up);
            Vector3 j2 = r1 * new Vector3(0f, g.d1, g.a1);
            Quaternion r2 = r1 * Quaternion.AngleAxis(q[1], Vector3.right);
            Vector3 j3 = j2 + r2 * new Vector3(0f, 0f, g.L2);
            Quaternion r3 = r2 * Quaternion.AngleAxis(q[2], Vector3.right);
            wrist = j3 + r3 * new Vector3(0f, 0f, g.L3);
            Quaternion r6 = r3 * Quaternion.AngleAxis(q[3], Vector3.forward) * Quaternion.AngleAxis(q[4], Vector3.right) *
                            Quaternion.AngleAxis(q[5], Vector3.forward);
            rot = r6;
            pos = wrist + r6 * new Vector3(0f, 0f, g.d6 + g.tool);
        }

        /// <summary>Key points of the arm in the base frame: [0] base, [1] shoulder J2, [2] elbow J3, [3] wrist centre, [4] flange, [5] TCP.</summary>
        public static void Points(RobotGeom g, float[] q, Vector3[] pts)
        {
            Quaternion r1 = Quaternion.AngleAxis(q[0], Vector3.up);
            pts[0] = Vector3.zero;
            pts[1] = r1 * new Vector3(0f, g.d1, g.a1);
            Quaternion r2 = r1 * Quaternion.AngleAxis(q[1], Vector3.right);
            pts[2] = pts[1] + r2 * new Vector3(0f, 0f, g.L2);
            Quaternion r3 = r2 * Quaternion.AngleAxis(q[2], Vector3.right);
            pts[3] = pts[2] + r3 * new Vector3(0f, 0f, g.L3);
            Quaternion r6 = r3 * Quaternion.AngleAxis(q[3], Vector3.forward) * Quaternion.AngleAxis(q[4], Vector3.right) *
                            Quaternion.AngleAxis(q[5], Vector3.forward);
            pts[4] = pts[3] + r6 * new Vector3(0f, 0f, g.d6);
            pts[5] = pts[4] + r6 * new Vector3(0f, 0f, g.tool);
        }

        static float Norm180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a <= -180f) a += 360f;
            return a;
        }

        /// <summary>Bring every joint into its limits, choosing the 360° alias nearest to the reference.</summary>
        static bool Wrap(RobotGeom g, float[] q, float[] reference)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = Norm180(q[i]);
                float target = reference != null ? reference[i] : 0f;
                float best = float.NaN, bestD = float.MaxValue;
                for (int k = -2; k <= 2; k++)
                {
                    float c = a + 360f * k;
                    if (c < g.qMin[i] || c > g.qMax[i]) continue;
                    float d = Mathf.Abs(c - target);
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (float.IsNaN(best)) return false;
                q[i] = best;
            }
            return true;
        }

        /// <summary>All (up to 8) analytic solutions inside the joint limits for a TCP pose in the base frame.</summary>
        public static int SolveAll(RobotGeom g, Vector3 p, Quaternion r, float[] reference, List<float[]> sols)
        {
            sols.Clear();
            Vector3 wc = p - r * new Vector3(0f, 0f, g.d6 + g.tool);
            float hd = Mathf.Sqrt(wc.x * wc.x + wc.z * wc.z);
            float qa = Mathf.Atan2(wc.x, wc.z) * Mathf.Rad2Deg;
            float h = wc.y - g.d1;
            for (int bi = 0; bi < 2; bi++)
            {
                if (hd < 1e-4f && bi == 1) break;
                float q0 = bi == 0 ? qa : qa + 180f;
                float rr = (bi == 0 ? hd : -hd) - g.a1;
                float D = (rr * rr + h * h - g.L2 * g.L2 - g.L3 * g.L3) / (2f * g.L2 * g.L3);
                if (D > 1f || D < -1f) continue;
                float relBase = Mathf.Acos(D);
                for (int ei = 0; ei < 2; ei++)
                {
                    float rel = ei == 0 ? relBase : -relBase;
                    float phi2 = Mathf.Atan2(h, rr) - Mathf.Atan2(g.L3 * Mathf.Sin(rel), g.L2 + g.L3 * Mathf.Cos(rel));
                    float q1 = -phi2 * Mathf.Rad2Deg, q2 = -rel * Mathf.Rad2Deg;
                    Quaternion r03 = Quaternion.AngleAxis(q0, Vector3.up) * Quaternion.AngleAxis(q1, Vector3.right) *
                                     Quaternion.AngleAxis(q2, Vector3.right);
                    Quaternion rw = Quaternion.Inverse(r03) * r;
                    Vector3 f = rw * Vector3.forward;
                    float b = Mathf.Acos(Mathf.Clamp(f.z, -1f, 1f));
                    for (int wi = 0; wi < 2; wi++)
                    {
                        float q4r = wi == 0 ? b : -b;
                        float s5 = Mathf.Sin(q4r);
                        bool singular = Mathf.Abs(s5) < 1e-4f;
                        float q3 = singular ? (reference != null ? reference[3] : 0f)
                                            : Mathf.Atan2(f.x / s5, -f.y / s5) * Mathf.Rad2Deg;
                        float q4 = q4r * Mathf.Rad2Deg;
                        Quaternion rz6 = Quaternion.Inverse(Quaternion.AngleAxis(q3, Vector3.forward) *
                                                            Quaternion.AngleAxis(q4, Vector3.right)) * rw;
                        Vector3 v = rz6 * Vector3.right;
                        float q5 = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                        var q = new[] { q0, q1, q2, q3, q4, q5 };
                        if (Wrap(g, q, reference)) sols.Add(q);
                        if (singular) break;
                    }
                }
            }
            return sols.Count;
        }

        /// <summary>Penalty for awkward postures: wrist singularity, joint-limit proximity, stretched elbow.</summary>
        public static float PostureCost(RobotGeom g, float[] q)
        {
            float c = 0f;
            float a5 = Mathf.Abs(q[4]);
            if (a5 < 15f) c += (15f - a5) * 3f;
            for (int i = 0; i < 6; i++)
            {
                float m = Mathf.Min(q[i] - g.qMin[i], g.qMax[i] - q[i]);
                if (m < 10f) c += (10f - m) * 2f;
            }
            float a3 = Mathf.Abs(q[2]);
            if (a3 < 20f) c += (20f - a3) * 3f;
            return c;
        }

        public static float Continuity(float[] a, float[] b)
        {
            float s = 0f;
            for (int i = 0; i < 6; i++) s += ContW[i] * Mathf.Abs(a[i] - b[i]);
            return s;
        }

        public static float MaxDelta(float[] a, float[] b, int count = 5)
        {
            float m = 0f;
            for (int i = 0; i < count; i++) m = Mathf.Max(m, Mathf.Abs(a[i] - b[i]));
            return m;
        }

        /// <summary>
        /// Best solution. continuity=true: closest to <paramref name="reference"/> (path following);
        /// otherwise the most comfortable posture (planning).
        /// </summary>
        public static bool Solve(RobotGeom g, Vector3 p, Quaternion r, float[] reference, bool continuity, float[] result, out float cost)
        {
            cost = float.MaxValue;
            SolveAll(g, p, r, continuity ? reference : null, buf);
            int best = -1;
            for (int i = 0; i < buf.Count; i++)
            {
                var q = buf[i];
                float c;
                if (continuity && reference != null) c = Continuity(q, reference) + 0.05f * PostureCost(g, q);
                else
                {
                    c = PostureCost(g, q) + 0.01f * (Mathf.Abs(q[1] + 60f) + Mathf.Abs(q[2] - 60f) + Mathf.Abs(q[4] + 90f) + Mathf.Abs(q[3]));
                }
                if (c < cost) { cost = c; best = i; }
            }
            if (best < 0) return false;
            System.Array.Copy(buf[best], result, 6);
            if (continuity) cost = PostureCost(g, result);
            return true;
        }
    }
}
