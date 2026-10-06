using UnityEngine;

namespace WeldingBot
{
    /// <summary>
    /// Plays a WeldSession. The timeline is analytic, so any simulated time can be evaluated directly:
    /// results are identical regardless of frame rate or speed multiplier.
    /// </summary>
    public class WeldSimRunner : MonoBehaviour
    {
        public GantryRig gantry;
        public RobotArm robot;
        public WeldEffects effects;
        public BeadRenderer beads;
        [Range(0.1f, 500f)] public float speed = 10f;
        public bool running;
        public float simTime;

        public WeldSession Session { get; private set; }
        public readonly SimState State = new SimState();
        public bool Finished => Session != null && simTime >= Session.timeline.Duration - 1e-4f;
        public float Duration => Session != null ? Session.timeline.Duration : 0f;

        public void SetSession(WeldSession s)
        {
            Session = s;
            simTime = 0f;
            running = false;
            Apply();
        }

        public void Play()
        {
            if (Session == null) return;
            if (Finished) simTime = 0f;
            running = true;
        }

        public void Pause() => running = false;

        public void ResetSim()
        {
            running = false;
            simTime = 0f;
            Apply();
        }

        public void Seek(float t)
        {
            simTime = Mathf.Clamp(t, 0f, Duration);
            Apply();
        }

        public void Advance(float seconds)
        {
            simTime = Mathf.Min(simTime + seconds, Duration);
            Apply();
        }

        void Update()
        {
            if (Session == null) return;
            if (running)
            {
                simTime += Time.unscaledDeltaTime * speed;
                if (simTime >= Duration) { simTime = Duration; running = false; }
            }
            Apply();
        }

        public void Apply()
        {
            Shader.SetGlobalFloat("_WB_SimTime", simTime);
            if (Session == null) return;
            Session.timeline.Evaluate(simTime, State);
            if (gantry != null) gantry.SetBase(State.basePos);
            if (robot != null) robot.SetJoints(State.q);
            if (effects != null) effects.SetArc(State.arc && running && Application.isPlaying);
            if (beads != null) beads.UpdateLines(simTime, State);
        }
    }
}
