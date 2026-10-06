using System.Collections.Generic;
using UnityEngine;

namespace WeldingBot
{
    public enum MotionKind { Idle, Park, GantryMove, AirMove, Approach, ArcStart, Weld, Crater, Retract }

    /// <summary>Complete machine state at one instant.</summary>
    public class SimState
    {
        public Vector3 basePos;                // robot base (gantry carriage) in world space
        public readonly float[] q = new float[6];
        public bool arc;
        public int seam = -1;                  // seam index being worked on (-1 = none)
        public float progress;                 // metres welded along the current seam (travel direction)
        public MotionKind kind;
        public int motionIndex;
    }

    public abstract class Motion
    {
        public MotionKind kind;
        public float t0, dur;
        public int seam = -1;
        public float T1 => t0 + dur;
        public abstract void Eval(float t, SimState st);

        protected float U(float t) => dur <= 1e-6f ? 1f : Mathf.Clamp01((t - t0) / dur);
        protected static float Smooth(float u) => u * u * (3f - 2f * u);
    }

    /// <summary>Robot joint interpolation with the gantry standing still.</summary>
    public class JointMove : Motion
    {
        public Vector3 basePos;
        public float[] qa, qb;
        public override void Eval(float t, SimState st)
        {
            float u = Smooth(U(t));
            st.basePos = basePos;
            for (int i = 0; i < 6; i++) st.q[i] = Mathf.Lerp(qa[i], qb[i], u);
            st.arc = false; st.kind = kind; st.seam = seam; st.progress = 0f;
        }
    }

    /// <summary>Robot holds a pose (optionally with the arc on).</summary>
    public class Dwell : Motion
    {
        public Vector3 basePos;
        public float[] q;
        public bool arc;
        public float progress;
        public override void Eval(float t, SimState st)
        {
            st.basePos = basePos;
            System.Array.Copy(q, st.q, 6);
            st.arc = arc; st.kind = kind; st.seam = seam; st.progress = progress;
        }
    }

    /// <summary>Gantry travel: lift to a safe height, move X/Z, lower. Robot parked.</summary>
    public class GantryMotion : Motion
    {
        public Vector3 a, b;
        public float safeY, tUp, tXZ, tDown;
        public float[] q;
        public override void Eval(float t, SimState st)
        {
            float lt = t - t0;
            Vector3 p;
            Vector3 up = new Vector3(a.x, safeY, a.z), over = new Vector3(b.x, safeY, b.z);
            if (lt < tUp) p = Vector3.Lerp(a, up, Smooth(tUp <= 0 ? 1 : lt / tUp));
            else if (lt < tUp + tXZ) p = Vector3.Lerp(up, over, Smooth(tXZ <= 0 ? 1 : (lt - tUp) / tXZ));
            else p = Vector3.Lerp(over, b, Smooth(tDown <= 0 ? 1 : Mathf.Clamp01((lt - tUp - tXZ) / tDown)));
            st.basePos = p;
            System.Array.Copy(q, st.q, 6);
            st.arc = false; st.kind = kind; st.seam = seam; st.progress = 0f;
        }
    }

    /// <summary>Path motion sampled in joint space (approach, weld, retract). Gantry may move linearly (tracking).</summary>
    public class SampledMotion : Motion
    {
        public Vector3 base0, base1;
        public float[][] qs;
        public bool arc;
        public float length;           // metres travelled by the TCP (for welds)
        public override void Eval(float t, SimState st)
        {
            float u = U(t);
            st.basePos = Vector3.Lerp(base0, base1, u);
            float f = u * (qs.Length - 1);
            int i = Mathf.Min((int)f, qs.Length - 2);
            float w = f - i;
            for (int k = 0; k < 6; k++) st.q[k] = Mathf.Lerp(qs[i][k], qs[i + 1][k], w);
            st.arc = arc; st.kind = kind; st.seam = seam;
            st.progress = kind == MotionKind.Weld ? length * u : 0f;
        }
    }

    public class Timeline
    {
        public readonly List<Motion> motions = new List<Motion>();
        public float Duration => motions.Count == 0 ? 0f : motions[motions.Count - 1].T1;

        public void Add(Motion m)
        {
            m.t0 = Duration;
            motions.Add(m);
        }

        public int IndexAt(float t)
        {
            int lo = 0, hi = motions.Count - 1;
            if (hi < 0) return -1;
            if (t >= motions[hi].t0) return hi;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (motions[mid].t0 <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public void Evaluate(float t, SimState st)
        {
            int i = IndexAt(t);
            if (i < 0) return;
            motions[i].Eval(Mathf.Min(t, motions[i].T1), st);
            st.motionIndex = i;
            if (t >= Duration) { st.kind = MotionKind.Idle; st.arc = false; st.seam = -1; }
        }
    }
}
