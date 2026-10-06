using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace WeldingBot.UI
{
    /// <summary>Left side panel built in code (UI Toolkit): job, start/pause/reset, speed, colours, camera, summary, seam list.</summary>
    [RequireComponent(typeof(UIDocument))]
    public class WeldingBotUI : MonoBehaviour
    {
        public AppController app;

        static readonly Color Bg = new Color(0.08f, 0.09f, 0.11f, 0.93f), Card = new Color(0.14f, 0.15f, 0.18f, 1f),
            Accent = new Color(1f, 0.55f, 0.12f), Text = new Color(0.92f, 0.93f, 0.95f), Dim = new Color(0.62f, 0.65f, 0.70f),
            Btn = new Color(0.22f, 0.24f, 0.28f), BtnOn = new Color(0.95f, 0.50f, 0.10f);

        VisualElement root, panel;
        DropdownField jobDrop;
        Label title, subtitle, jobDesc, jobInfo, speedLabel, status, timeLabel, legend, hint;
        Label hJob, hSpeed, hColor, hCam, hLang, hProgress;
        Button startBtn, resetBtn, langKo, langEn, camOver, camFollow;
        readonly Dictionary<ColorMode, Button> colorBtns = new Dictionary<ColorMode, Button>();
        Slider speedSlider;
        VisualElement progressFill;
        Foldout summaryFold, seamFold;
        VisualElement summaryRows, seamRows;
        readonly List<Label> seamLabels = new List<Label>();
        float nextRefresh;
        bool built;

        static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        public static string Hms(float s)
        {
            int t = Mathf.RoundToInt(s);
            return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
        }

        void OnEnable()
        {
            Loc.Changed += Rebuild;
            Build();
        }

        void OnDisable() { Loc.Changed -= Rebuild; }

        void Start()
        {
            if (app != null)
            {
                app.JobLoaded += OnJobLoaded;
                if (app.cameraRig != null) app.cameraRig.isOverUI = IsOverPanel;
            }
            OnJobLoaded();
        }

        void Rebuild() { Build(); OnJobLoaded(); }

        public bool IsOverPanel(Vector2 screenPos)
        {
            if (panel == null || panel.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel.panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            return panel.worldBound.Contains(p);
        }

        // ---------- construction ----------

        static void Pad(VisualElement e, float v) { e.style.paddingLeft = e.style.paddingRight = e.style.paddingTop = e.style.paddingBottom = v; }
        static void Radius(VisualElement e, float r) { e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r; }

        static Label L(string text, float size = 13, Color? c = null, bool bold = false)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = c ?? Text;
            l.style.whiteSpace = WhiteSpace.Normal;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 2;
            return l;
        }

        static Button B(string text, System.Action onClick, float grow = 1f)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = grow;
            b.style.height = 30;
            b.style.fontSize = 13;
            b.style.color = Text;
            b.style.backgroundColor = Btn;
            b.style.borderLeftWidth = b.style.borderRightWidth = b.style.borderTopWidth = b.style.borderBottomWidth = 0;
            Radius(b, 5);
            b.style.marginLeft = b.style.marginRight = 2;
            return b;
        }

        static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.marginBottom = 4;
            return r;
        }

        VisualElement Section(VisualElement parent, out Label header, string text)
        {
            var c = new VisualElement();
            c.style.backgroundColor = Card;
            Radius(c, 7);
            Pad(c, 9);
            c.style.marginBottom = 8;
            header = L(text, 12, Dim, true);
            header.style.marginBottom = 5;
            c.Add(header);
            parent.Add(c);
            return c;
        }

        void Build()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null || doc.rootVisualElement == null) return;
            root = doc.rootVisualElement;
            root.Clear();
            colorBtns.Clear();
            seamLabels.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.flexDirection = FlexDirection.Row;

            panel = new VisualElement();
            panel.style.width = 370;
            panel.style.backgroundColor = Bg;
            Pad(panel, 10);
            root.Add(panel);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            panel.Add(scroll);

            title = L("WeldingBot", 22, Accent, true);
            subtitle = L(Loc.T("Shipyard welding robot simulator"), 13, Dim);
            subtitle.style.marginBottom = 10;
            scroll.Add(title); scroll.Add(subtitle);

            // job
            var sj = Section(scroll, out hJob, Loc.T("Job"));
            var names = new List<string>();
            foreach (var n in JobPresets.Names) names.Add(Loc.T(n));
            int cur = app != null ? System.Array.IndexOf(JobPresets.Names, app.jobName) : 0;
            jobDrop = new DropdownField(names, Mathf.Max(0, cur));
            jobDrop.style.fontSize = 13;
            jobDrop.RegisterValueChangedCallback(e =>
            {
                int i = names.IndexOf(e.newValue);
                if (i >= 0 && app != null) app.LoadJob(JobPresets.Names[i]);
            });
            sj.Add(jobDrop);
            jobDesc = L("", 12, Dim); sj.Add(jobDesc);
            jobInfo = L("", 12, Text); sj.Add(jobInfo);
            var r1 = Row(); r1.style.marginTop = 6;
            startBtn = B(Loc.T("Start"), OnStartPause);
            startBtn.style.backgroundColor = BtnOn;
            resetBtn = B(Loc.T("Reset"), () => app?.ResetWeld());
            r1.Add(startBtn); r1.Add(resetBtn);
            sj.Add(r1);

            // progress
            var sp = Section(scroll, out hProgress, Loc.T("Progress"));
            var bar = new VisualElement();
            bar.style.height = 10; bar.style.backgroundColor = Btn; Radius(bar, 5);
            bar.style.marginBottom = 6;
            progressFill = new VisualElement();
            progressFill.style.height = 10; progressFill.style.backgroundColor = Accent; Radius(progressFill, 5);
            progressFill.style.width = Length.Percent(0);
            bar.Add(progressFill);
            sp.Add(bar);
            status = L("", 14, Text, true); sp.Add(status);
            timeLabel = L("", 12, Dim); sp.Add(timeLabel);

            // speed
            var ss = Section(scroll, out hSpeed, Loc.T("Simulation speed"));
            speedLabel = L("", 13, Text, true);
            ss.Add(speedLabel);
            speedSlider = new Slider(0f, Mathf.Log10(500f));
            speedSlider.RegisterValueChangedCallback(e => SetSpeed(Mathf.Pow(10f, e.newValue)));
            ss.Add(speedSlider);
            var r2 = Row();
            foreach (var x in new[] { 1f, 10f, 50f, 200f, 500f })
            {
                float v = x;
                r2.Add(B($"{x:0}x", () => SetSpeed(v)));
            }
            ss.Add(r2);

            // colours
            var sc = Section(scroll, out hColor, Loc.T("Bead colour"));
            var r3 = Row();
            foreach (ColorMode m in System.Enum.GetValues(typeof(ColorMode)))
            {
                var mm = m;
                var b = B(Loc.T(m == ColorMode.Heat ? "Heat" : m == ColorMode.Position ? "Position" : "Joint type"), () => SetColorMode(mm));
                colorBtns[m] = b; r3.Add(b);
            }
            sc.Add(r3);
            legend = L("", 12, Dim);
            legend.enableRichText = true;
            sc.Add(legend);

            // camera
            var scam = Section(scroll, out hCam, Loc.T("Camera"));
            var r4 = Row();
            camOver = B(Loc.T("Overview"), () => { if (app?.cameraRig != null) { app.cameraRig.FrameBounds(app.Session.job.Bounds()); } RefreshButtons(); });
            camFollow = B(Loc.T("Follow torch"), () => { app?.cameraRig?.SetMode(CameraRig.Mode.FollowTorch); RefreshButtons(); });
            r4.Add(camOver); r4.Add(camFollow);
            scam.Add(r4);
            hint = L(Loc.T("Left drag: rotate · Right drag: pan · Wheel: zoom"), 11, Dim);
            scam.Add(hint);

            // summary
            summaryFold = new Foldout { text = Loc.T("Summary"), value = true };
            StyleFold(summaryFold);
            summaryRows = new VisualElement();
            summaryFold.Add(summaryRows);
            scroll.Add(summaryFold);

            seamFold = new Foldout { text = Loc.T("Seams"), value = false };
            StyleFold(seamFold);
            seamRows = new VisualElement();
            seamFold.Add(seamRows);
            scroll.Add(seamFold);

            // language
            var sl = Section(scroll, out hLang, Loc.T("Language"));
            var r5 = Row();
            langEn = B("English", () => Loc.Korean = false);
            langKo = B("한국어", () => Loc.Korean = true);
            r5.Add(langEn); r5.Add(langKo);
            sl.Add(r5);

            built = true;
            if (app != null && app.runner != null) speedSlider.SetValueWithoutNotify(Mathf.Log10(Mathf.Max(1f, app.runner.speed)));
            RefreshButtons();
        }

        void StyleFold(Foldout f)
        {
            f.style.backgroundColor = Card;
            Radius(f, 7);
            Pad(f, 6);
            f.style.marginBottom = 8;
            f.style.color = Text;
            f.style.fontSize = 13;
        }

        // ---------- actions ----------

        void OnStartPause()
        {
            if (app == null || app.runner == null) return;
            if (app.runner.running) app.Pause(); else app.StartWeld();
            RefreshButtons();
        }

        void SetSpeed(float v)
        {
            if (app == null || app.runner == null) return;
            app.runner.speed = Mathf.Clamp(v, 1f, 500f);
            speedSlider.SetValueWithoutNotify(Mathf.Log10(app.runner.speed));
            RefreshButtons();
        }

        void SetColorMode(ColorMode m)
        {
            if (app?.beads == null) return;
            app.beads.SetColorMode(m);
            RefreshButtons();
        }

        void OnJobLoaded()
        {
            if (!built || app == null || app.Session == null) return;
            var s = app.Session;
            int idx = System.Array.IndexOf(JobPresets.Names, s.job.name);
            if (idx >= 0) jobDrop.SetValueWithoutNotify(jobDrop.choices[idx]);
            jobDesc.text = Loc.T(s.job.description);
            int fil = 0, butt = 0;
            foreach (var se in s.seams) if (se.type == JointType.Fillet) fil++; else butt++;
            jobInfo.text = Loc.F("{0} seams: {1} fillet, {2} butt, {3:0.0} m", s.seams.Count, fil, butt, s.stats.seamLength) + "\n" +
                           (s.stats.unreachable.Count > 0 ? Loc.F("{0} unreachable", s.stats.unreachable.Count) : Loc.T("all seams reachable")) +
                           $" · {Loc.T("Planned time")} {Hms(s.stats.totalTime)}";
            seamRows.Clear();
            seamLabels.Clear();
            foreach (var se in s.seams)
            {
                var l = L("", 12, Text);
                l.enableRichText = true;
                seamRows.Add(l);
                seamLabels.Add(l);
            }
            nextRefresh = 0f;
            RefreshButtons();
            Refresh();
        }

        void RefreshButtons()
        {
            if (!built || app == null || app.runner == null) return;
            var r = app.runner;
            startBtn.text = r.running ? Loc.T("Pause") : (r.simTime > 0f && !r.Finished ? Loc.T("Resume") : Loc.T("Start"));
            speedLabel.text = $"{r.speed:0.#} x";
            float sv = Mathf.Log10(Mathf.Max(1f, r.speed));
            if (Mathf.Abs(speedSlider.value - sv) > 0.001f) speedSlider.SetValueWithoutNotify(sv);
            var mode = app.beads != null ? app.beads.Mode : ColorMode.Heat;
            foreach (var kv in colorBtns) kv.Value.style.backgroundColor = kv.Key == mode ? BtnOn : Btn;
            bool follow = app.cameraRig != null && app.cameraRig.mode == CameraRig.Mode.FollowTorch;
            camFollow.style.backgroundColor = follow ? BtnOn : Btn;
            camOver.style.backgroundColor = follow ? Btn : BtnOn;
            langKo.style.backgroundColor = Loc.Korean ? BtnOn : Btn;
            langEn.style.backgroundColor = Loc.Korean ? Btn : BtnOn;
            legend.text = Legend(mode);
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        static string Legend(ColorMode m)
        {
            switch (m)
            {
                case ColorMode.Position:
                    var p = BeadRenderer.PositionColors;
                    return $"<color=#{Hex(p[0])}>■</color> {Loc.T("Flat")}  <color=#{Hex(p[1])}>■</color> {Loc.T("Horizontal")}  " +
                           $"<color=#{Hex(p[2])}>■</color> {Loc.T("Vertical")}  <color=#{Hex(p[3])}>■</color> {Loc.T("Overhead")}";
                case ColorMode.JointType:
                    return $"<color=#33CCFF>■</color> {Loc.T("Fillet")}  <color=#FFCC26>■</color> {Loc.T("Butt")}";
                default:
                    return $"<color=#FFF2C0>■</color>→<color=#FF7310>■</color>→<color=#1AE6BF>■</color>  {Loc.T("Heat")}";
            }
        }

        // ---------- live refresh ----------

        void Update()
        {
            if (!built || app == null || app.runner == null || app.Session == null) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void AddRow(string k, string v)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.justifyContent = Justify.SpaceBetween;
            var a = L(k, 12, Dim); var b = L(v, 12, Text, true);
            r.Add(a); r.Add(b);
            summaryRows.Add(r);
        }

        void Refresh()
        {
            var r = app.runner; var s = app.Session;
            if (r == null || s == null) return;
            if (app.cameraRig != null && root != null && root.worldBound.width > 1f)
                app.cameraRig.viewportLeft = Mathf.Clamp(panel.worldBound.width / root.worldBound.width, 0f, 0.5f);
            float dur = Mathf.Max(r.Duration, 1e-3f);
            progressFill.style.width = Length.Percent(100f * r.simTime / dur);
            var st = r.State;
            string what = r.Finished ? Loc.T("Finished") : (r.simTime <= 0f && !r.running ? Loc.T("Ready") : Loc.T(st.kind.ToString()));
            if (!r.Finished && st.seam >= 0 && st.seam < s.seams.Count) what += $"  ·  {s.seams[st.seam].name}";
            status.text = what;
            timeLabel.text = $"{Hms(r.simTime)} / {Hms(r.Duration)}";
            RefreshButtons();

            var live = s.StatsAt(r.simTime);
            summaryRows.Clear();
            AddRow(Loc.T("Total time"), $"{Hms(live.totalTime)} / {Hms(s.stats.totalTime)}");
            AddRow(Loc.T("Arc time"), Hms(live.arcTime));
            AddRow(Loc.T("Arc-on ratio"), $"{(live.totalTime > 0 ? 100f * live.arcTime / live.totalTime : 0f):0.0} % ({100f * s.stats.ArcRatio:0.0} %)");
            AddRow(Loc.T("Welded length"), $"{F1(live.weldedLength)} / {F1(s.stats.plannedLength)} m");
            AddRow(Loc.T("Seams done"), $"{live.seamsDone} / {s.stats.seamsPlanned}");
            AddRow(Loc.T("Unreachable"), s.stats.unreachable.Count.ToString());
            AddRow(Loc.T("Gantry moves"), $"{s.stats.gantryMoves}");
            AddRow(Loc.T("Gantry travel"), $"{F1(s.stats.gantryDistance)} m");
            AddRow(Loc.T("Gantry time"), $"{Hms(live.gantryTime)}");
            AddRow(Loc.T("Fixed stations"), $"{s.stats.stations}");
            AddRow(Loc.T("Tracked seams"), $"{s.stats.trackedSeams}");
            AddRow(Loc.T("Robot air time"), Hms(live.airTime + live.approachTime));
            AddRow(Loc.T("Wire used"), $"{live.wireKg:0.00} / {s.stats.wireKg:0.00} kg");
            AddRow(Loc.T("By position"),
                $"1:{F1(live.lengthByPosition[0])} 2:{F1(live.lengthByPosition[1])} 3:{F1(live.lengthByPosition[2])} 4:{F1(live.lengthByPosition[3])} m");
            AddRow(Loc.T("By joint"), $"{Loc.T("Fillet")} {F1(live.lengthByType[0])} · {Loc.T("Butt")} {F1(live.lengthByType[1])} m");
            AddRow(Loc.T("Plan computed in"), $"{s.stats.planningMs:0} ms");

            if (seamFold.value)
            {
                for (int i = 0; i < seamLabels.Count && i < s.seams.Count; i++)
                {
                    var se = s.seams[i];
                    string mark; Color c;
                    if (!s.weldMotionOf.TryGetValue(i, out var wm)) { mark = "×"; c = BeadRenderer.UnreachableLine; }
                    else if (r.simTime >= wm.T1) { mark = "●"; c = BeadRenderer.DoneLine; }
                    else if (st.seam == i && !r.Finished) { mark = "▶"; c = BeadRenderer.ActiveLine; }
                    else { mark = "○"; c = Dim; }
                    string mode = s.planOf.TryGetValue(i, out var sp) ? Loc.T(sp.mode == WeldMode.Fixed ? "fixed" : "track") : Loc.T("n/a");
                    seamLabels[i].text = $"<color=#{Hex(c)}>{mark}</color> {se.name}  {Loc.T(se.type.ToString())} {se.PositionCode}  {se.Length:0.00} m  {se.angleDeg:0}°  · {mode}";
                }
            }
        }
    }
}
