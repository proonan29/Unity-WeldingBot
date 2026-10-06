using UnityEngine;

namespace WeldingBot
{
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
}
