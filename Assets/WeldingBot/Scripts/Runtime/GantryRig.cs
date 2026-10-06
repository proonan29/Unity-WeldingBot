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
}
