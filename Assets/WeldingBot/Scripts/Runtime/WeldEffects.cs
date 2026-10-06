using UnityEngine;

namespace WeldingBot
{
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
