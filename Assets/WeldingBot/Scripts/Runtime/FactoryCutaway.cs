using UnityEngine;

namespace WeldingBot
{
    /// <summary>Hides the roof / walls that stand between an outside camera and the hall interior.</summary>
    public class FactoryCutaway : MonoBehaviour
    {
        public GameObject roof, north, south, east, west;

        public void UpdateFor(Vector3 cam)
        {
            Set(roof, cam.y < FactoryBuilder.Eave - 0.3f);
            Set(north, cam.z < FactoryBuilder.HallZ);
            Set(south, cam.z > -FactoryBuilder.HallZ);
            Set(east, cam.x < FactoryBuilder.HallX);
            Set(west, cam.x > -FactoryBuilder.HallX);
        }

        static void Set(GameObject g, bool visible)
        {
            if (g == null) return;
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                if (r.enabled != visible) r.enabled = visible;
        }
    }
}
