using UnityEngine;

namespace WeldingBot
{
    /// <summary>Loads jobs (plates → seams → plan → beads) and exposes start / pause / reset to the UI and CLI.</summary>
    public class AppController : MonoBehaviour
    {
        public string jobName = "Stiffened Panel";
        public Transform jobRoot;
        public Material plateMat, jigMat, beadMat, lineMat;
        public WeldSimRunner runner;
        public BeadRenderer beads;
        public CameraRig cameraRig;
        public RobotGeom geom = new RobotGeom();
        public GantryLimits limits = new GantryLimits();
        public WeldParams weld = new WeldParams();

        public WeldSession Session => runner != null ? runner.Session : null;
        public event System.Action JobLoaded;

        void Start()
        {
            if (Application.isPlaying) LoadJob(jobName);
        }

        public WeldSession LoadJob(string name)
        {
            var job = JobPresets.Get(name);
            jobName = job.name;
            if (runner != null) runner.Pause();
            PlateBuilder.Build(job, jobRoot, plateMat, jigMat);
            var session = WeldSession.Build(job, geom, limits, weld);
            beads.Build(session, beadMat, lineMat);
            runner.SetSession(session);
            if (cameraRig != null) cameraRig.FrameBounds(job.Bounds());
            JobLoaded?.Invoke();
            return session;
        }

        public void StartWeld() => runner.Play();
        public void Pause() => runner.Pause();
        public void ResetWeld() => runner.ResetSim();
    }
}
