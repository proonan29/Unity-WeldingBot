using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeldingBot
{
    public enum ColorMode { Heat = 0, Position = 1, JointType = 2 }

    /// <summary>
    /// Weld beads (one mesh per seam, revealed by the shader as simulated time passes) and the seam guide lines.
    /// Generated objects are never saved with the scene: they are rebuilt from the job on load.
    /// </summary>
    public class BeadRenderer : MonoBehaviour
    {
        public static readonly Color[] PositionColors =
        {
            new Color(0.30f, 0.85f, 0.35f), new Color(0.25f, 0.60f, 1.00f), new Color(1.00f, 0.55f, 0.15f), new Color(0.85f, 0.35f, 0.95f)
        };
        public static readonly Color PendingLine = new Color(0.80f, 0.82f, 0.88f), ActiveLine = new Color(1f, 0.55f, 0.1f),
            UnreachableLine = new Color(1f, 0.12f, 0.12f), DoneLine = new Color(0.1f, 0.9f, 0.75f);

        public float visualScale = 2.2f;
        public float minVisualLeg = 0.014f;
        public float ringStep = 0.01f;

        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly List<Mesh> meshes = new List<Mesh>();
        WeldSession session;
        Transform root;
        public ColorMode Mode { get; private set; }

        public void SetColorMode(ColorMode m)
        {
            Mode = m;
            Shader.SetGlobalFloat("_WB_ColorMode", (float)m);
        }

        public void Clear()
        {
            foreach (var m in meshes) if (m != null) DestroyImmediate(m);
            meshes.Clear();
            lines.Clear();
            if (root != null) DestroyImmediate(root.gameObject);
            var old = transform.Find("Welds");
            if (old != null) DestroyImmediate(old.gameObject);
            root = null;
        }

        public void Build(WeldSession s, Material beadMat, Material lineMat)
        {
            Clear();
            session = s;
            root = new GameObject("Welds").transform;
            root.SetParent(transform, false);
            root.gameObject.hideFlags = HideFlags.DontSave;
            foreach (var seam in s.seams)
            {
                var go = new GameObject("Bead_" + seam.name);
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(root, false);
                var mesh = BuildMesh(seam);
                mesh.hideFlags = HideFlags.DontSave;
                meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = beadMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;

                var lg = new GameObject("Line_" + seam.name);
                lg.hideFlags = HideFlags.DontSave;
                lg.transform.SetParent(root, false);
                var lr = lg.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMat;
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                Vector3 lift = -seam.torchDir * 0.006f;
                lr.SetPosition(0, seam.p0 + lift);
                lr.SetPosition(1, seam.p1 + lift);
                lr.widthMultiplier = 0.012f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lines.Add(lr);
            }
            SetColorMode(Mode);
        }

        Mesh BuildMesh(Seam s)
        {
            const int K = 7;
            float L = s.Length;
            int rings = Mathf.Max(2, Mathf.CeilToInt(L / ringStep) + 1);
            Vector3 t = s.Dir;
            float leg = Mathf.Max(s.leg * visualScale, minVisualLeg);
            float bulge = leg * (s.type == JointType.Fillet ? 0.28f : 0.35f);
            Vector3 outDir = -s.torchDir;
            bool reversed = session.planOf.TryGetValue(s.id, out var sp) && sp.reversed;
            Color posCol = PositionColors[(int)s.position];

            var verts = new List<Vector3>(rings * K);
            var norms = new List<Vector3>(rings * K);
            var cols = new List<Color>(rings * K);
            var info = new List<Vector4>(rings * K);
            var offs = new List<Vector4>(rings * K);
            var profile = new Vector3[K];
            var pnorm = new Vector3[K];
            for (int k = 0; k < K; k++)
            {
                float f = k / (float)(K - 1);
                profile[k] = (s.u1 * (1f - f) + s.u2 * f) * leg + outDir * (bulge * Mathf.Sin(Mathf.PI * f) + 0.0015f);
            }
            for (int k = 0; k < K; k++)
            {
                Vector3 tang = profile[Mathf.Min(k + 1, K - 1)] - profile[Mathf.Max(k - 1, 0)];
                Vector3 n = Vector3.Cross(tang, t).normalized;
                if (Vector3.Dot(n, outDir) < 0f) n = -n;
                pnorm[k] = n;
            }
            for (int i = 0; i < rings; i++)
            {
                float so = Mathf.Min(i * ringStep, L);
                Vector3 c = s.p0 + t * so;
                float dep = session.DepositTime(s.id, so);
                int predIdx = reversed ? i + 1 : i - 1;
                bool first = predIdx < 0 || predIdx >= rings;
                float sPred = first ? so : Mathf.Min(predIdx * ringStep, L);
                float prev = first ? dep - 0.001f : session.DepositTime(s.id, sPred);
                Vector3 off = first ? Vector3.zero : t * (sPred - so);
                for (int k = 0; k < K; k++)
                {
                    verts.Add(c + profile[k]);
                    norms.Add(pnorm[k]);
                    cols.Add(posCol);
                    info.Add(new Vector4(dep, prev, s.type == JointType.Butt ? 1f : 0f, 0f));
                    offs.Add(new Vector4(off.x, off.y, off.z, 0f));
                }
            }
            var tris = new List<int>((rings - 1) * (K - 1) * 6);
            for (int i = 0; i < rings - 1; i++)
            for (int k = 0; k < K - 1; k++)
            {
                int a = i * K + k, b = a + 1, c = a + K, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
            var mesh = new Mesh { name = "Bead_" + s.name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(cols);
            mesh.SetUVs(1, info);
            mesh.SetUVs(2, offs);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds((s.p0 + s.p1) * 0.5f, new Vector3(Mathf.Abs(s.p1.x - s.p0.x), Mathf.Abs(s.p1.y - s.p0.y), Mathf.Abs(s.p1.z - s.p0.z)) + Vector3.one * (leg * 4f + 0.05f));
            return mesh;
        }

        /// <summary>Seam guide line colours: pending / active / done / unreachable.</summary>
        public void UpdateLines(float simTime, SimState st)
        {
            if (session == null) return;
            for (int i = 0; i < lines.Count && i < session.seams.Count; i++)
            {
                var lr = lines[i];
                if (lr == null) continue;
                Color c; float w = 0.012f;
                if (!session.weldMotionOf.TryGetValue(i, out var wm)) { c = UnreachableLine; w = 0.02f; }
                else if (simTime >= wm.T1) { c = DoneLine; w = 0.004f; }
                else if (st != null && st.seam == i) { c = ActiveLine; w = 0.016f; }
                else c = PendingLine;
                lr.startColor = lr.endColor = c;
                lr.widthMultiplier = w;
            }
        }
    }
}
