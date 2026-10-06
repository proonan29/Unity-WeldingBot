using System.Threading.Tasks;
using UnityEngine;

namespace WeldingBot
{
    /// <summary>Loads jobs (plates → seams → plan → beads) and exposes start / pause / reset to the UI and CLI.
    /// In play mode the plan is computed on a background thread so the screen keeps running.</summary>
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
        public event System.Action PlanningStarted;

        // background planning state
        Task<WeldSession> planTask;
        WeldPlanner.Context planCtx;
        WeldJob planJob;
        public bool IsPlanning => planTask != null;
        public string PlanningJob => planJob != null ? planJob.name : null;
        public float PlanningProgress => planCtx == null || planCtx.progressTotal <= 0 ? 0f
            : Mathf.Clamp01(planCtx.progressDone / (float)planCtx.progressTotal);
        public string LastError { get; private set; }

        void Start()
        {
            if (Application.isPlaying) LoadJobAsync(jobName);
        }

        void OnDestroy() { CancelPlanning(); }

        /// <summary>Synchronous load (edit mode, CLI, tests).</summary>
        public WeldSession LoadJob(string name)
        {
            CancelPlanning();
            var job = JobPresets.Get(name);
            jobName = job.name;
            if (runner != null) runner.Pause();
            PlateBuilder.Build(job, jobRoot, plateMat, jigMat);
            var session = WeldSession.Build(job, geom, limits, weld);
            Apply(session);
            if (cameraRig != null) cameraRig.FrameBounds(job.Bounds());
            return session;
        }

        /// <summary>Show the plates immediately and compute the plan on a worker thread.</summary>
        public void LoadJobAsync(string name)
        {
            CancelPlanning();
            var job = JobPresets.Get(name);
            jobName = job.name;
            if (runner != null) { runner.Pause(); runner.SetSession(null); }
            if (beads != null) beads.Clear();
            PlateBuilder.Build(job, jobRoot, plateMat, jigMat);
            if (cameraRig != null) cameraRig.FrameBounds(job.Bounds());
            planJob = job;
            planCtx = new WeldPlanner.Context();
            var ctx = planCtx;
            var g = geom; var lim = limits; var wp = weld;
            LastError = null;
            planTask = Task.Run(() => WeldSession.Build(job, g, lim, wp, ctx));
            PlanningStarted?.Invoke();
        }

        public void CancelPlanning()
        {
            if (planCtx != null) planCtx.cancel = true;
            planTask = null; planCtx = null; planJob = null;
        }

        /// <summary>Block until the background plan is done (CLI / tests).</summary>
        public WeldSession WaitForPlan(int timeoutMs = 60000)
        {
            if (planTask == null) return Session;
            if (!planTask.Wait(timeoutMs)) return null;
            Update();
            return Session;
        }

        void Update()
        {
            if (planTask == null || !planTask.IsCompleted) return;
            var t = planTask;
            planTask = null; planCtx = null; planJob = null;
            if (t.IsFaulted)
            {
                LastError = t.Exception?.GetBaseException().Message;
                Debug.LogError("WeldingBot planning failed: " + t.Exception?.GetBaseException());
                return;
            }
            if (t.IsCanceled) return;
            Apply(t.Result);
        }

        void Apply(WeldSession session)
        {
            beads.Build(session, beadMat, lineMat);
            runner.SetSession(session);
            if (cameraRig != null && !Application.isPlaying) cameraRig.FrameBounds(session.job.Bounds());
            JobLoaded?.Invoke();
        }

        public void StartWeld() { if (!IsPlanning) runner.Play(); }
        public void Pause() => runner.Pause();
        public void ResetWeld() => runner.ResetSim();
    }
}
