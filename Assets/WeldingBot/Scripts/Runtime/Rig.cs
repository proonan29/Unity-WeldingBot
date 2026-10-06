using UnityEngine;

namespace WeldingBot
{
    /// <summary>Portal gantry: bridge travels X on floor rails, trolley travels Z on the bridge, mast telescopes Y.</summary>
    public class GantryRig : MonoBehaviour
    {
        public Transform bridge;      // moves in X
        public Transform trolley;     // child of bridge, moves in Z
        public Transform mount;       // robot base flange (child of trolley), moves in Y
        public Transform mastInner;   // scaled column between sleeve bottom and mount
        public float trolleyY = 8.95f;
        public float sleeveBottomY = 7.0f;

        public Vector3 BasePosition => mount != null ? mount.position : Vector3.zero;

        public void SetBase(Vector3 b)
        {
            if (bridge == null) return;
            bridge.localPosition = new Vector3(b.x, 0f, 0f);
            trolley.localPosition = new Vector3(0f, trolleyY, b.z);
            mount.localPosition = new Vector3(0f, b.y - trolleyY, 0f);
            if (mastInner != null)
            {
                float top = sleeveBottomY + 0.6f, bottom = b.y + 0.05f;
                float len = Mathf.Max(0.1f, top - bottom);
                mastInner.localPosition = new Vector3(0f, (top + bottom) * 0.5f - trolleyY, 0f);
                mastInner.localScale = new Vector3(0.30f, len, 0.30f);
            }
        }
    }

    /// <summary>Robot joint transforms (built by RobotBuilder). Base is mounted upside down under the gantry mount.</summary>
    public class RobotArm : MonoBehaviour
    {
        public RobotGeom geom = new RobotGeom();
        public Transform[] joints = new Transform[6];
        public Transform tcp;
        public readonly float[] q = (float[])RobotGeom.Home.Clone();

        public void SetJoints(float[] values)
        {
            for (int i = 0; i < 6; i++)
            {
                q[i] = values[i];
                if (joints[i] != null) joints[i].localRotation = Quaternion.AngleAxis(values[i], RobotKinematics.Axis(i));
            }
        }
    }

    /// <summary>Arc light, sparks and nozzle glow at the TCP.</summary>
    public class WeldEffects : MonoBehaviour
    {
        public ParticleSystem sparks;
        public Light arcLight;
        public Renderer glow;
        bool on;

        public void SetArc(bool arc)
        {
            if (arc == on) return;
            on = arc;
            if (sparks != null)
            {
                var em = sparks.emission;
                em.enabled = arc;
                if (arc && !sparks.isPlaying) sparks.Play();
            }
            if (arcLight != null) arcLight.enabled = arc;
            if (glow != null) glow.enabled = arc;
        }

        void Update()
        {
            if (!on) return;
            if (arcLight != null) arcLight.intensity = 3f + Random.value * 4f;
            if (glow != null) glow.transform.localScale = Vector3.one * (0.02f + Random.value * 0.015f);
        }
    }
}
