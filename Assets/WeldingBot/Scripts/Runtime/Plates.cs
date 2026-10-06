using System.Collections.Generic;
using UnityEngine;

namespace WeldingBot
{
    /// <summary>A rectangular steel plate: local X = length, local Z = width, local Y = thickness (normal).</summary>
    [System.Serializable]
    public class PlateSpec
    {
        public string name;
        public Vector3 center;
        public Quaternion rotation = Quaternion.identity;
        public float length, width, thickness;
        public bool weldable = true;      // false = jig / support (never welded, no seams)

        public Vector3 Right => rotation * Vector3.right;
        public Vector3 Normal => rotation * Vector3.up;
        public Vector3 Fwd => rotation * Vector3.forward;
        public float HX => length * 0.5f;
        public float HZ => width * 0.5f;
        public float HT => thickness * 0.5f;

        public Vector3 ToLocal(Vector3 w)
        {
            var d = w - center;
            return new Vector3(Vector3.Dot(d, Right), Vector3.Dot(d, Normal), Vector3.Dot(d, Fwd));
        }

        public bool InRect(Vector3 w, float eps)
        {
            var l = ToLocal(w);
            return Mathf.Abs(l.x) <= HX + eps && Mathf.Abs(l.z) <= HZ + eps;
        }

        public Bounds WorldBounds()
        {
            var b = new Bounds(center, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = center + Right * (((i & 1) == 0 ? -1 : 1) * HX) + Normal * (((i & 2) == 0 ? -1 : 1) * HT) +
                        Fwd * (((i & 4) == 0 ? -1 : 1) * HZ);
                b.Encapsulate(c);
            }
            return b;
        }

        /// <summary>The 4 thin edges as mid-thickness lines with their outward in-plane direction.</summary>
        public PlateEdge[] Edges()
        {
            Vector3 R = Right * HX, F = Fwd * HZ;
            return new[]
            {
                new PlateEdge { a = center + R - F, b = center + R + F, outward = Right },
                new PlateEdge { a = center - R - F, b = center - R + F, outward = -Right },
                new PlateEdge { a = center - R + F, b = center + R + F, outward = Fwd },
                new PlateEdge { a = center - R - F, b = center + R - F, outward = -Fwd },
            };
        }
    }

    public struct PlateEdge
    {
        public Vector3 a, b, outward;
        public Vector3 Mid => (a + b) * 0.5f;
    }

    public class WeldJob
    {
        public string name;          // English key (also the CLI name)
        public string description;   // English (translated by Loc)
        public List<PlateSpec> plates = new List<PlateSpec>();

        public Bounds Bounds()
        {
            var b = plates.Count > 0 ? plates[0].WorldBounds() : new Bounds();
            foreach (var p in plates) b.Encapsulate(p.WorldBounds());
            return b;
        }
    }

    /// <summary>Helpers to place plates the way a fitter would: on the platen, standing on another plate, or hinged edge-to-edge.</summary>
    public class JobBuilder
    {
        public const float PlatenTop = 0.40f;
        public readonly WeldJob job;
        public Vector3 offset;
        int n;

        public JobBuilder(WeldJob job, Vector3 offset) { this.job = job; this.offset = offset; }

        string Name(string prefix) => $"{prefix}{++n:00}";

        PlateSpec Add(PlateSpec p) { job.plates.Add(p); return p; }

        /// <summary>Plate lying flat on the platen (yaw about Y).</summary>
        public PlateSpec Flat(float cx, float cz, float length, float width, float t, float yaw = 0f, float y = PlatenTop)
        {
            return Add(new PlateSpec
            {
                name = Name("P"), center = offset + new Vector3(cx, y + t * 0.5f, cz),
                rotation = Quaternion.Euler(0f, yaw, 0f), length = length, width = width, thickness = t
            });
        }

        /// <summary>Plate given by its mid-plane centre, local X direction (length) and local Z direction (width).</summary>
        public PlateSpec Panel(Vector3 center, Vector3 xDir, Vector3 zDir, float length, float width, float t, bool weldable = true, string prefix = "P")
        {
            zDir = zDir.normalized;
            xDir = Vector3.ProjectOnPlane(xDir, zDir).normalized;
            return Add(new PlateSpec
            {
                name = Name(weldable ? prefix : "J"), center = offset + center,
                rotation = Quaternion.LookRotation(zDir, Vector3.Cross(zDir, xDir)),
                length = length, width = width, thickness = t, weldable = weldable
            });
        }

        /// <summary>Jig block (not welded).</summary>
        public PlateSpec Support(Vector3 center, Vector3 size)
        {
            return Add(new PlateSpec
            {
                name = Name("J"), center = offset + center, rotation = Quaternion.identity,
                length = size.x, width = size.z, thickness = size.y, weldable = false
            });
        }

        static Vector3 TopNormal(PlateSpec b) => b.Normal * (b.Normal.y >= 0f ? 1f : -1f);

        /// <summary>Point on the base's top surface above/below world (x, z) (offset applied).</summary>
        public Vector3 OnTop(PlateSpec b, float x, float z)
        {
            Vector3 nU = TopNormal(b);
            Vector3 p0 = b.center + nU * b.HT;
            x += offset.x; z += offset.z;
            float y = p0.y - ((x - p0.x) * nU.x + (z - p0.z) * nU.z) / nU.y;
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Web (stiffener / wall) standing on the base plate along the line (x0,z0)-(x1,z1) on its top surface,
        /// height h, leaning by leanDeg about the line (0 = perpendicular to the base).
        /// </summary>
        public PlateSpec WebOn(PlateSpec b, float x0, float z0, float x1, float z1, float h, float t, float leanDeg = 0f)
        {
            Vector3 A = OnTop(b, x0, z0), B = OnTop(b, x1, z1);
            Vector3 l = (B - A).normalized;
            Vector3 f = Quaternion.AngleAxis(leanDeg, l) * TopNormal(b);
            return Add(new PlateSpec
            {
                name = Name("W"), center = (A + B) * 0.5f + f * (h * 0.5f),
                rotation = Quaternion.LookRotation(f, Vector3.Cross(f, l)),
                length = (B - A).magnitude, width = h, thickness = t
            });
        }

        /// <summary>Plate hinged on a line (mid-thickness), extending from it along dir (perpendicular to the hinge).</summary>
        public PlateSpec Hinged(Vector3 hingeA, Vector3 hingeB, Vector3 dir, float width, float t)
        {
            Vector3 l = (hingeB - hingeA).normalized;
            dir = Vector3.ProjectOnPlane(dir, l).normalized;
            return Add(new PlateSpec
            {
                name = Name("P"), center = offset + (hingeA + hingeB) * 0.5f + dir * (width * 0.5f),
                rotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, l)),
                length = (hingeB - hingeA).magnitude, width = width, thickness = t
            });
        }
    }

    public static class JobPresets
    {
        public static readonly string[] Names = { "Stiffened Panel", "Inclined Webs", "Angled Panels", "Box Block", "Assembly Line" };

        public static WeldJob Get(string name)
        {
            foreach (var n in Names)
                if (string.Equals(n, name, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Replace(" ", ""), name.Replace(" ", "").Replace("_", ""), System.StringComparison.OrdinalIgnoreCase))
                    name = n;
            var job = new WeldJob { name = name };
            switch (name)
            {
                case "Stiffened Panel":
                    job.description = "Two deck plates butt-welded, four stiffeners fillet-welded on both sides.";
                    StiffenedPanel(new JobBuilder(job, Vector3.zero)); break;
                case "Inclined Webs":
                    job.description = "Webs leaning 0-40 degrees and a diagonal web: torch angle follows the joint.";
                    InclinedWebs(new JobBuilder(job, Vector3.zero)); break;
                case "Angled Panels":
                    job.description = "Edge-to-edge joints at angles: ridge, valley and a butt on an inclined panel.";
                    AngledPanels(new JobBuilder(job, Vector3.zero)); break;
                case "Box Block":
                    job.description = "Box with walls, bulkhead and a floor stiffener: horizontal and vertical fillets.";
                    BoxBlock(new JobBuilder(job, Vector3.zero)); break;
                case "Assembly Line":
                    job.description = "All four jobs along the platen: the gantry travels between work areas.";
                    StiffenedPanel(new JobBuilder(job, new Vector3(-14.5f, 0f, 0f)));
                    InclinedWebs(new JobBuilder(job, new Vector3(-4.5f, 0f, 0f)));
                    AngledPanels(new JobBuilder(job, new Vector3(5.0f, 0f, 0f)));
                    BoxBlock(new JobBuilder(job, new Vector3(14.0f, 0f, 0f)));
                    break;
                default:
                    return Get(Names[0]);
            }
            return job;
        }

        static void StiffenedPanel(JobBuilder j)
        {
            const float t = 0.012f, tw = 0.010f;
            var a = j.Flat(-2f, 0f, 4f, 3f, t);
            var b = j.Flat(2f, 0f, 4f, 3f, t);
            foreach (var x in new[] { -3f, -1f }) j.WebOn(a, x, -1.3f, x, 1.3f, 0.30f, tw);
            foreach (var x in new[] { 1f, 3f }) j.WebOn(b, x, -1.3f, x, 1.3f, 0.30f, tw);
        }

        static void InclinedWebs(JobBuilder j)
        {
            const float tw = 0.010f;
            var b = j.Flat(0f, 0f, 6.4f, 3f, 0.014f);
            j.WebOn(b, -2.4f, -1.2f, -2.4f, 1.2f, 0.35f, tw, 0f);
            j.WebOn(b, -1.2f, -1.2f, -1.2f, 1.2f, 0.35f, tw, 20f);
            j.WebOn(b, 0.1f, -1.2f, 0.1f, 1.2f, 0.35f, tw, -30f);
            j.WebOn(b, 1.3f, -1.2f, 1.3f, 1.2f, 0.35f, tw, 40f);
            j.WebOn(b, 2.0f, -1.2f, 2.8f, 1.2f, 0.30f, tw, 0f);
        }

        static void AngledPanels(JobBuilder j)
        {
            const float t = 0.012f;
            // Ridge: two plates meeting at the top, sloping down 30 deg
            {
                float s = Mathf.Sin(30f * Mathf.Deg2Rad), c = Mathf.Cos(30f * Mathf.Deg2Rad);
                var hA = new Vector3(-4.2f, 1.30f, -1.4f); var hB = new Vector3(-1.4f, 1.30f, -1.4f);
                j.Hinged(hA, hB, new Vector3(0f, -s, -c), 1.0f, t);
                j.Hinged(hA, hB, new Vector3(0f, -s, c), 1.0f, t);
                float yLow = 1.30f - s * 1.0f;
                foreach (var dz in new[] { -c, c })
                foreach (var x in new[] { -3.8f, -1.8f })
                    j.Support(new Vector3(x, (JobBuilder.PlatenTop + yLow) * 0.5f - 0.01f, -1.4f + dz * 0.95f), new Vector3(0.12f, yLow - JobBuilder.PlatenTop - 0.03f, 0.12f));
            }
            // Valley: two plates rising 25 deg from a low hinge
            {
                float s = Mathf.Sin(25f * Mathf.Deg2Rad), c = Mathf.Cos(25f * Mathf.Deg2Rad);
                var hA = new Vector3(1.4f, 0.75f, -1.4f); var hB = new Vector3(4.2f, 0.75f, -1.4f);
                j.Hinged(hA, hB, new Vector3(0f, s, -c), 1.0f, t);
                j.Hinged(hA, hB, new Vector3(0f, s, c), 1.0f, t);
                j.Support(new Vector3(2.8f, (JobBuilder.PlatenTop + 0.75f) * 0.5f - 0.02f, -1.4f), new Vector3(2.4f, 0.75f - JobBuilder.PlatenTop - 0.05f, 0.12f));
            }
            // Butt joint on a panel inclined 20 deg (joint line climbs the slope)
            {
                float a = 20f * Mathf.Deg2Rad;
                Vector3 up = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));      // in-plane, climbing toward +z
                Vector3 baseMid = new Vector3(0f, 0.75f, 0.9f);
                float w = 1.6f;
                Vector3 c0 = baseMid + up * (w * 0.5f);
                // two halves meeting at x = 0, edge-to-edge
                j.Panel(c0 + new Vector3(-1.2f, 0f, 0f), Vector3.right, up, 2.4f, w, t);
                j.Panel(c0 + new Vector3(1.2f, 0f, 0f), Vector3.right, up, 2.4f, w, t);
                float yTop = 0.75f + Mathf.Sin(a) * w;
                j.Support(new Vector3(0f, (JobBuilder.PlatenTop + 0.75f) * 0.5f - 0.01f, 0.9f), new Vector3(4.6f, 0.75f - JobBuilder.PlatenTop - 0.03f, 0.12f));
                j.Support(new Vector3(0f, (JobBuilder.PlatenTop + yTop) * 0.5f - 0.01f, 0.9f + Mathf.Cos(a) * w - 0.08f), new Vector3(4.6f, yTop - JobBuilder.PlatenTop - 0.06f, 0.12f));
            }
        }

        static void BoxBlock(JobBuilder j)
        {
            const float t = 0.012f, tw = 0.012f, zw = 1.1f, h = 0.8f;
            var b = j.Flat(0f, 0f, 3.4f, 2.8f, 0.014f);
            // longitudinal walls (z = +-1.1), 0.8 m high
            j.WebOn(b, -1.45f, -zw, 1.45f, -zw, h, tw);
            j.WebOn(b, -1.45f, zw, 1.45f, zw, h, tw);
            // transverse bulkhead between the walls (its side edges sit on the wall faces)
            float zIn = zw - tw * 0.5f;
            j.WebOn(b, -1.3f, -zIn, -1.3f, zIn, h, t);
            // floor stiffener from the bulkhead face toward +x
            j.WebOn(b, -1.3f + t * 0.5f, 0f, 1.2f, 0f, 0.25f, 0.010f);
        }
    }
}
