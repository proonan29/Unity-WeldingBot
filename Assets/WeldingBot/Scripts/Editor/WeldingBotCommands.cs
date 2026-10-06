using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using WeldingBot.UI;

namespace WeldingBot.EditorTools
{
    /// <summary>Material provider that persists materials as assets (so the saved scene references real files).</summary>
    public class AssetMaterialProvider : IMaterialProvider
    {
        public const string Folder = "Assets/WeldingBot/Materials";

        public Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = Prims.NewLit(key, color, smoothness, metallic);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        public static Material GetWithShader(string key, string shaderName, Color color)
        {
            EnsureFolder(Folder);
            string path = $"{Folder}/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var sh = shaderName.StartsWith("Assets/") ? AssetDatabase.LoadAssetAtPath<Shader>(shaderName) : Shader.Find(shaderName);
            if (sh == null) sh = Prims.LitShader;
            m = new Material(sh) { name = key };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>CLI commands for the WeldingBot simulator: `unity command wb_*`.</summary>
    public static class WeldingBotCommands
    {
        const string ScenePath = "Assets/WeldingBot/Scenes/WeldingBot.unity";
        const string BeadShaderPath = "Assets/WeldingBot/Shaders/WeldBead.shader";
        const string UiFolder = "Assets/WeldingBot/UI";
        const string PanelSettingsPath = UiFolder + "/WeldingBotPanelSettings.asset";
        const string ThemePath = UiFolder + "/WeldingBotTheme.tss";
        const string TextSettingsPath = UiFolder + "/WeldingBotTextSettings.asset";
        const string FontPath = UiFolder + "/Fonts/NotoSansKR-Regular.otf";
        const string FontAssetPath = UiFolder + "/Fonts/NotoSansKR-Regular SDF.asset";
        const string VolumePath = "Assets/WeldingBot/Settings/WeldingBotVolume.asset";

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";

        // ------------------------------------------------------------------ scene

        [CliCommand("wb_list_jobs", "List the built-in welding jobs.")]
        public static string ListJobs() => string.Join(", ", JobPresets.Names);

        [CliCommand("wb_setup_scene", "Create the WeldingBot scene: factory hall, portal gantry, inverted 6-axis robot with torch, job plates, simulation, camera and UI. Saves the scene.")]
        public static string SetupScene(
            [CliArg("job", "Job preset (wb_list_jobs)")] string job = "Stiffened Panel",
            [CliArg("scene_path", "Scene asset path")] string scenePath = ScenePath)
        {
            if (Application.isPlaying) return "error: exit play mode first";
            var mats = new AssetMaterialProvider();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(62f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.49f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.29f, 0.28f);
            RenderSettings.sun = light;

            var world = new GameObject("World").transform;
            var factory = FactoryBuilder.Build(world, mats);
            var rig = GantryBuilder.Build(world, mats);
            var geom = new RobotGeom();
            var arm = RobotBuilder.Build(rig.mount, mats, geom);
            var sparkMat = AssetMaterialProvider.GetWithShader("Sparks", "Universal Render Pipeline/Particles/Unlit", new Color(1f, 0.8f, 0.35f));
            var glowMat = AssetMaterialProvider.GetWithShader("ArcGlow", "Universal Render Pipeline/Unlit", new Color(0.85f, 0.92f, 1f));
            var fx = RobotBuilder.BuildEffects(arm.tcp, sparkMat, glowMat);

            var jobRoot = new GameObject("Job").transform;
            var simGo = new GameObject("Simulation");
            var runner = simGo.AddComponent<WeldSimRunner>();
            var beads = simGo.AddComponent<BeadRenderer>();
            runner.gantry = rig; runner.robot = arm; runner.effects = fx; runner.beads = beads;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var camRig = camGo.AddComponent<CameraRig>();
            camRig.torch = arm.tcp;
            camRig.cutaway = factory.GetComponent<FactoryCutaway>();
            EnsureVolume();

            var appGo = new GameObject("App");
            var app = appGo.AddComponent<AppController>();
            app.jobRoot = jobRoot;
            app.runner = runner;
            app.beads = beads;
            app.cameraRig = camRig;
            app.geom = geom;
            app.plateMat = mats.Get("PlateSteel", new Color(0.50f, 0.54f, 0.58f), 0.45f, 0.7f);
            app.jigMat = mats.Get("Jig", new Color(0.20f, 0.20f, 0.22f), 0.3f, 0.5f);
            app.beadMat = AssetMaterialProvider.GetWithShader("WeldBead", BeadShaderPath, Color.white);
            app.lineMat = AssetMaterialProvider.GetWithShader("SeamLine", "Universal Render Pipeline/Particles/Unlit", Color.white);
            EnsureUI(app);

            var session = app.LoadJob(job);
            AssetMaterialProvider.EnsureFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, scenePath);
            AddSceneToBuild(scenePath);
            AssetDatabase.SaveAssets();
            return $"scene={scenePath} " + Summary(session);
        }

        static void AddSceneToBuild(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void EnsureVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                AssetMaterialProvider.EnsureFolder(Path.GetDirectoryName(VolumePath).Replace('\\', '/'));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1.1f);
                bloom.intensity.Override(0.8f);
                bloom.scatter.Override(0.6f);
                var tone = profile.Add<Tonemapping>(true);
                tone.mode.Override(TonemappingMode.Neutral);
                foreach (var c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var go = new GameObject("PostProcess");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static AppController App(out string error)
        {
            error = null;
            var app = Object.FindAnyObjectByType<AppController>();
            if (app == null) error = "error: no AppController in the active scene (wb_setup_scene)";
            return app;
        }

        static WeldSession EnsureSession(AppController app)
        {
            if (app.Session == null) app.LoadJob(app.jobName);
            return app.Session;
        }

        [CliCommand("wb_load_job", "Load a job into the scene (edit mode: also saves the scene; play mode: runtime switch).")]
        public static string LoadJob([CliArg("job", "Job preset (wb_list_jobs)")] string job)
        {
            var app = App(out var e); if (app == null) return e;
            var s = app.LoadJob(job);
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
                EditorSceneManager.SaveScene(app.gameObject.scene);
            }
            return Summary(s);
        }

        // ------------------------------------------------------------------ analysis (no scene changes)

        public static string Summary(WeldSession s)
        {
            var st = s.stats;
            var un = string.Join(" ", st.unreachable.Select(i => s.seams[i].name));
            return $"job={s.job.name} plates={s.job.plates.Count(p => p.weldable)} seams={st.seams} " +
                   $"(fillet={s.seams.Count(x => x.type == JointType.Fillet)} butt={s.seams.Count(x => x.type == JointType.Butt)}) " +
                   $"length={F(st.seamLength)}m planned={st.seamsPlanned} unreachable={st.unreachable.Count}[{un}] " +
                   $"total={WeldingBotUI.Hms(st.totalTime)} arc={WeldingBotUI.Hms(st.arcTime)} arc_ratio={F(100f * st.ArcRatio)}% " +
                   $"gantry_moves={st.gantryMoves} gantry_dist={F(st.gantryDistance)}m gantry_time={WeldingBotUI.Hms(st.gantryTime)} " +
                   $"stations={st.stations} tracked={st.trackedSeams} air={WeldingBotUI.Hms(st.airTime)} wire={F(st.wireKg)}kg " +
                   $"pos[1/2/3/4]={F(st.lengthByPosition[0])}/{F(st.lengthByPosition[1])}/{F(st.lengthByPosition[2])}/{F(st.lengthByPosition[3])}m " +
                   $"plan_ms={F(st.planningMs)} unsafe_air_moves={st.unsafeAirMoves} collision_checks={s.ctx.collisionChecks} rejected={s.ctx.collisionHits}";
        }

        static WeldSession Headless(string job)
        {
            var app = Object.FindAnyObjectByType<AppController>();
            var geom = app != null ? app.geom : new RobotGeom();
            var lim = app != null ? app.limits : new GantryLimits();
            var wp = app != null ? app.weld : new WeldParams();
            return WeldSession.Build(JobPresets.Get(job), geom, lim, wp);
        }

        [CliCommand("wb_seams", "Extract and list the weld seams of a job (headless).")]
        public static string Seams([CliArg("job", "Job preset")] string job = "Stiffened Panel")
        {
            var j = JobPresets.Get(job);
            var seams = SeamExtractor.Extract(j.plates);
            var sb = new StringBuilder($"job={j.name} plates={j.plates.Count} seams={seams.Count}\n");
            foreach (var s in seams)
                sb.AppendLine($"{s.name} {s.type} {s.PositionCode} {j.plates[s.plateA].name}-{j.plates[s.plateB].name} len={F(s.Length)} " +
                              $"angle={F(s.angleDeg)} p0={V(s.p0)} p1={V(s.p1)} torch={V(s.torchDir)} leg={F(s.leg * 1000f)}mm");
            return sb.ToString().TrimEnd();
        }

        [CliCommand("wb_plan", "Plan a job headless: welding order, fixed/tracking mode, gantry station per seam.")]
        public static string Plan([CliArg("job", "Job preset")] string job = "Stiffened Panel")
        {
            var s = Headless(job);
            var sb = new StringBuilder(Summary(s) + "\n");
            foreach (var sp in s.order)
            {
                var se = s.seams[sp.seam];
                var wm = s.weldMotionOf[sp.seam];
                sb.AppendLine($"{se.name} {se.type} {se.PositionCode} len={F(se.Length)} rev={sp.reversed} mode={sp.mode} base0={V(sp.base0)} base1={V(sp.base1)} " +
                              $"angles={F(sp.ang0)}/{F(sp.ang1)} speed={F(sp.speed * 1000f)}mm/s weld_t0={WeldingBotUI.Hms(wm.t0)} dur={F(wm.dur)}s cost={F(sp.cost)}");
            }
            return sb.ToString().TrimEnd();
        }

        [CliCommand("wb_sim_run", "Run every job (or one) headless and report the summary; checks the timeline (IK reconstruction error along each weld).")]
        public static string SimRun([CliArg("job", "Job preset or 'all'")] string job = "all")
        {
            var sb = new StringBuilder();
            var names = job == "all" ? JobPresets.Names : new[] { job };
            foreach (var n in names)
            {
                var s = Headless(n);
                sb.AppendLine(Summary(s) + " " + VerifyTimeline(s));
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Samples the timeline and compares the FK of the robot with the seam path while the arc is on.</summary>
        static string VerifyTimeline(WeldSession s)
        {
            var st = new SimState();
            float maxErr = 0f, maxAng = 0f, maxJump = 0f;
            float[] prev = null;
            var lim = s.ctx.lim;
            bool limitsOk = true;
            int n = 0, collide = 0, airCollide = 0;
            float dt = 0.05f;
            for (float t = 0f; t <= s.timeline.Duration; t += dt)
            {
                s.timeline.Evaluate(t, st);
                if (!lim.Inside(st.basePos)) limitsOk = false;
                if (prev != null) maxJump = Mathf.Max(maxJump, RobotKinematics.MaxDelta(prev, st.q, 6) / dt);
                prev = (float[])st.q.Clone();
                bool path = st.kind == MotionKind.Weld || st.kind == MotionKind.Approach || st.kind == MotionKind.Retract;
                if (WeldPlanner.Collides(s.ctx, st.basePos, st.q)) { if (path) collide++; else airCollide++; }
                if (st.kind != MotionKind.Weld) continue;
                var sp = s.planOf[st.seam];
                Vector3 target = Vector3.Lerp(sp.p0, sp.p1, st.progress / sp.length);
                RobotKinematics.FK(s.ctx.geom, st.q, out var lp, out var lr, out _);
                Vector3 wp = st.basePos + WeldPlanner.BaseRotation * lp;
                Quaternion wr = WeldPlanner.BaseRotation * lr;
                maxErr = Mathf.Max(maxErr, (wp - target).magnitude);
                maxAng = Mathf.Max(maxAng, Vector3.Angle(wr * Vector3.forward, sp.AxisAt(st.progress / sp.length)));
                n++;
            }
            return $"verify: samples={n} tcp_err_max={F(maxErr * 1000f)}mm axis_err_max={F(maxAng)}deg max_joint_speed={F(maxJump)}deg/s gantry_in_limits={limitsOk} collision_samples={collide} air_move_collision_samples={airCollide}";
        }

        [CliCommand("wb_ik_test", "Random FK -> IK -> FK round trips for the robot kinematics.")]
        public static string IkTest([CliArg("n", "Number of random poses")] int n = 2000, [CliArg("seed", "Random seed")] int seed = 7)
        {
            var g = new RobotGeom();
            var rnd = new System.Random(seed);
            float maxPos = 0f, maxAng = 0f; int fail = 0, foundOriginal = 0;
            var sols = new System.Collections.Generic.List<float[]>();
            var q = new float[6]; var r = new float[6];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 6; k++)
                {
                    float lo = Mathf.Max(g.qMin[k], -170f), hi = Mathf.Min(g.qMax[k], 170f);
                    q[k] = lo + (float)rnd.NextDouble() * (hi - lo);
                }
                if (Mathf.Abs(q[4]) < 5f) q[4] = 20f;
                RobotKinematics.FK(g, q, out var p, out var rot, out _);
                RobotKinematics.SolveAll(g, p, rot, q, sols);
                if (sols.Count == 0) { fail++; continue; }
                bool orig = false;
                foreach (var s in sols)
                {
                    RobotKinematics.FK(g, s, out var p2, out var r2, out _);
                    maxPos = Mathf.Max(maxPos, (p2 - p).magnitude);
                    maxAng = Mathf.Max(maxAng, Quaternion.Angle(r2, rot));
                    if (RobotKinematics.MaxDelta(s, q, 6) < 0.05f) orig = true;
                }
                if (orig) foundOriginal++;
                if (!RobotKinematics.Solve(g, p, rot, null, false, r, out _)) fail++;
            }
            return $"n={n} fail={fail} original_recovered={foundOriginal}/{n} max_pos_err={F(maxPos * 1000f)}mm max_rot_err={F(maxAng)}deg";
        }

        // ------------------------------------------------------------------ edit-mode preview

        [CliCommand("wb_preview", "Edit or play mode: show the simulation state at a time (seconds, or fraction 0-1 with --fraction true). Not saved.")]
        public static string Preview([CliArg("t", "Time")] float t = 1f, [CliArg("fraction", "Interpret t as a fraction of the job")] bool fraction = true,
            [CliArg("camera", "overview | follow | keep")] string camera = "keep")
        {
            var app = App(out var e); if (app == null) return e;
            var s = EnsureSession(app);
            float time = fraction ? t * s.timeline.Duration : t;
            app.runner.Seek(time);
            if (camera == "overview") app.cameraRig.FrameBounds(s.job.Bounds());
            else if (camera == "follow") { app.cameraRig.SetMode(CameraRig.Mode.FollowTorch); app.cameraRig.Place(); }
            var st = app.runner.State;
            return $"t={WeldingBotUI.Hms(time)} kind={st.kind} seam={(st.seam >= 0 ? s.seams[st.seam].name : "-")} base={V(st.basePos)} " +
                   $"q=[{string.Join(",", st.q.Select(F))}] tcp={V(app.runner.robot.tcp.position)}";
        }

        [CliCommand("wb_seek_seam", "Edit or play mode: jump to a point inside the weld of a seam (e.g. S08 at 0.5).")]
        public static string SeekSeam([CliArg("seam", "Seam name, e.g. S08")] string seam, [CliArg("f", "Fraction of the weld 0-1")] float f = 0.5f,
            [CliArg("camera", "follow | overview | keep")] string camera = "follow")
        {
            var app = App(out var e); if (app == null) return e;
            var s = EnsureSession(app);
            var se = s.seams.Find(x => x.name.Equals(seam, System.StringComparison.OrdinalIgnoreCase));
            if (se == null) return $"error: no seam {seam}";
            if (!s.weldMotionOf.TryGetValue(se.id, out var m)) return $"{se.name} is not planned (unreachable)";
            return Preview(m.t0 + Mathf.Clamp01(f) * m.dur, false, camera);
        }

        [CliCommand("wb_camera", "Set the camera: overview | follow | orbit with yaw/pitch/distance (edit or play mode).")]
        public static string Camera([CliArg("mode", "overview | follow | orbit")] string mode = "overview",
            [CliArg("yaw", "deg")] float yaw = -35f, [CliArg("pitch", "deg")] float pitch = 32f, [CliArg("distance", "m (<=0 keeps)")] float distance = 0f,
            [CliArg("pivot", "x,y,z (empty keeps)")] string pivot = "")
        {
            var app = App(out var e); if (app == null) return e;
            var rig = app.cameraRig;
            if (mode == "follow") rig.SetMode(CameraRig.Mode.FollowTorch);
            else if (mode == "overview") { EnsureSession(app); rig.FrameBounds(app.Session.job.Bounds()); }
            else rig.mode = CameraRig.Mode.Overview;
            rig.yaw = yaw; rig.pitch = pitch;
            if (distance > 0f) { if (rig.mode == CameraRig.Mode.FollowTorch) rig.followDistance = distance; else rig.distance = distance; }
            if (!string.IsNullOrEmpty(pivot))
            {
                var p = pivot.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                rig.pivot = new Vector3(p[0], p[1], p[2]);
            }
            rig.Place();
            return $"camera mode={rig.mode} pos={V(rig.transform.position)} yaw={F(rig.yaw)} pitch={F(rig.pitch)}";
        }

        [CliCommand("wb_color_mode", "Bead colour mode: heat | position | type.")]
        public static string ColorModeCmd([CliArg("mode", "heat | position | type")] string mode = "heat")
        {
            var app = App(out var e); if (app == null) return e;
            var m = mode.StartsWith("p") ? ColorMode.Position : mode.StartsWith("t") ? ColorMode.JointType : ColorMode.Heat;
            app.beads.SetColorMode(m);
            return $"color_mode={m}";
        }

        // ------------------------------------------------------------------ play mode

        static AppController PlayApp(out string error)
        {
            error = null;
            if (!Application.isPlaying) { error = "error: enter play mode first (editor_play)"; return null; }
            return App(out error);
        }

        [CliCommand("wb_sim_start", "Play mode: start (or resume) welding.")]
        public static string SimStart([CliArg("speed", "Speed multiplier (<=0 keeps)")] float speed = 0f)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            if (speed > 0f) app.runner.speed = speed;
            app.StartWeld();
            return Status();
        }

        [CliCommand("wb_sim_pause", "Play mode: pause.")]
        public static string SimPause()
        {
            var app = PlayApp(out var e); if (app == null) return e;
            app.Pause();
            return Status();
        }

        [CliCommand("wb_sim_reset", "Play mode: reset to the start.")]
        public static string SimReset()
        {
            var app = PlayApp(out var e); if (app == null) return e;
            app.ResetWeld();
            return Status();
        }

        [CliCommand("wb_sim_speed", "Play mode: simulation speed multiplier (1-500).")]
        public static string SimSpeed([CliArg("x", "Speed multiplier")] float x)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            app.runner.speed = Mathf.Clamp(x, 0.1f, 500f);
            return Status();
        }

        [CliCommand("wb_sim_advance", "Play mode: advance N simulated seconds immediately (independent of window focus).")]
        public static string SimAdvance([CliArg("seconds", "Simulated seconds")] float seconds = 60f)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            app.runner.Advance(seconds);
            return Status();
        }

        [CliCommand("wb_sim_status", "Play mode: live status and statistics.")]
        public static string Status()
        {
            var app = App(out var e); if (app == null) return e;
            var r = app.runner; var s = r.Session;
            if (s == null) return "no session";
            var live = s.StatsAt(r.simTime);
            var st = r.State;
            return $"running={r.running} finished={r.Finished} speed={F(r.speed)}x t={WeldingBotUI.Hms(r.simTime)}/{WeldingBotUI.Hms(r.Duration)} " +
                   $"kind={st.kind} seam={(st.seam >= 0 ? s.seams[st.seam].name : "-")} arc={st.arc} base={V(st.basePos)} " +
                   $"welded={F(live.weldedLength)}/{F(s.stats.plannedLength)}m seams_done={live.seamsDone}/{s.stats.seamsPlanned} " +
                   $"arc_time={WeldingBotUI.Hms(live.arcTime)} wire={F(live.wireKg)}kg";
        }

        // ------------------------------------------------------------------ UI

        static PanelSettings EnsurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null) return ps;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            return ps;
        }

        static void EnsureUI(AppController app)
        {
            var ui = Object.FindAnyObjectByType<WeldingBotUI>();
            if (ui == null)
            {
                var go = new GameObject("UI");
                go.AddComponent<UIDocument>();
                ui = go.AddComponent<WeldingBotUI>();
            }
            var doc = ui.GetComponent<UIDocument>();
            doc.panelSettings = EnsurePanelSettings();
            ui.app = app;
            SetupFont(false);
            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(ui);
        }

        [CliCommand("wb_setup_font", "Create the Noto Sans KR dynamic font asset and register it as a fallback for the UI.")]
        public static string SetupFont([CliArg("force", "Recreate the font asset")] bool force = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) return $"error: font not found: {FontPath}";
            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fa != null && force) { AssetDatabase.DeleteAsset(FontAssetPath); fa = null; }
            if (fa == null)
            {
                fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (fa == null) return "error: CreateFontAsset failed";
                fa.name = "NotoSansKR-Regular SDF";
                fa.isMultiAtlasTexturesEnabled = true;
                AssetDatabase.CreateAsset(fa, FontAssetPath);
                if (fa.atlasTextures != null)
                    foreach (var tex in fa.atlasTextures)
                        if (tex != null) { tex.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
                if (fa.material != null) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                EditorUtility.SetDirty(fa);
            }
            var ts = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (ts == null) { ts = ScriptableObject.CreateInstance<PanelTextSettings>(); AssetDatabase.CreateAsset(ts, TextSettingsPath); }
            if (ts.fallbackFontAssets == null) ts.fallbackFontAssets = new System.Collections.Generic.List<FontAsset>();
            if (!ts.fallbackFontAssets.Contains(fa)) ts.fallbackFontAssets.Add(fa);
            ts.clearDynamicDataOnBuild = true;
            EditorUtility.SetDirty(ts);
            var ps = EnsurePanelSettings();
            ps.textSettings = ts;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return $"font={FontAssetPath} hangul={fa.HasCharacter('한', true, true)}";
        }
    }
}
