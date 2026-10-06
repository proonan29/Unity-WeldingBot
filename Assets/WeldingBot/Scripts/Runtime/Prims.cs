using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeldingBot
{
    public interface IMaterialProvider
    {
        Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f);
    }

    /// <summary>In-memory materials (play mode / previews).</summary>
    public class RuntimeMaterialProvider : IMaterialProvider
    {
        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        public Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = Prims.NewLit(key, color, smoothness, metallic);
            cache[key] = m;
            return m;
        }
    }

    /// <summary>Primitive helpers (no colliders – nothing here uses physics).</summary>
    public static class Prims
    {
        public static Shader LitShader
        {
            get
            {
                var s = Shader.Find("Universal Render Pipeline/Lit");
                return s != null ? s : Shader.Find("Standard");
            }
        }

        public static Material NewLit(string name, Color color, float smoothness, float metallic)
        {
            var m = new Material(LitShader) { name = name };
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }

        static void Strip(GameObject g)
        {
            var c = g.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m,
            Quaternion? rot = null, bool castShadows = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(g);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = size;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!castShadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        /// <summary>Cylinder whose axis is the local Y axis of <paramref name="rot"/>.</summary>
        public static GameObject Cyl(string name, Transform parent, Vector3 localPos, float radius, float height,
            Material m, Quaternion? rot = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Strip(g);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        /// <summary>Cylinder along local Z.</summary>
        public static GameObject CylZ(string name, Transform parent, Vector3 localPos, float radius, float length, Material m)
            => Cyl(name, parent, localPos, radius, length, m, Quaternion.Euler(90f, 0f, 0f));

        /// <summary>Cylinder along local X.</summary>
        public static GameObject CylX(string name, Transform parent, Vector3 localPos, float radius, float length, Material m)
            => Cyl(name, parent, localPos, radius, length, m, Quaternion.Euler(0f, 0f, 90f));

        public static Transform Node(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
        }
    }
}
