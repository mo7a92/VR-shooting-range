using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRRange.Core;

namespace VRRange.Editor
{
    /// <summary>
    /// Places downloaded CC0 props (Assets/Art/Props/&lt;name&gt;/&lt;name&gt;.fbx + texture set, see Tools/AssetGen/fetch_polyhaven.py)
    /// with their PBR material, static flags, a box collider and a surface tag. Props are grounded on request so a model
    /// whose pivot is not at its base still sits on the floor.
    /// </summary>
    public static class PropLibrary
    {
        static readonly Dictionary<string, GameObject> s_Models = new Dictionary<string, GameObject>();
        public static int Placed { get; private set; }
        public static readonly List<string> Missing = new List<string>();

        public static void Reset() { s_Models.Clear(); Placed = 0; Missing.Clear(); }

        public static bool Exists(string name) => Load(name) != null;

        static GameObject Load(string name)
        {
            if (s_Models.TryGetValue(name, out var m) && m != null) return m;
            m = AssetDatabase.LoadAssetAtPath<GameObject>($"{RangePaths.ArtProps}/{name}/{name}.fbx");
            s_Models[name] = m;
            return m;
        }

        /// <summary>Expected size (metres) from the asset's SOURCE.txt "Dimensions (mm)" line (Poly Haven metadata), or null.</summary>
        static Vector3? ExpectedSize(string name)
        {
            try
            {
                var path = $"{RangePaths.ArtProps}/{name}/SOURCE.txt";
                if (!System.IO.File.Exists(path)) return null;
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    if (!line.StartsWith("Dimensions")) continue;
                    var m = System.Text.RegularExpressions.Regex.Matches(line, @"[-+]?\d*\.?\d+");
                    if (m.Count >= 3) return new Vector3(float.Parse(m[0].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m[1].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m[2].Value, System.Globalization.CultureInfo.InvariantCulture)) / 1000f;
                }
            }
            catch { }
            return null;
        }

        public static Material MaterialFor(string name, float emission = 0f, Color? tint = null) => MaterialLibrary.PbrFromFolder($"{RangePaths.ArtProps}/{name}", name, $"Prop_{name}", 0.5f, tint, emission);

        /// <summary>Instantiates a prop. <paramref name="groundY"/>: when set, the model is shifted so its lowest point sits at that height.</summary>
        public static GameObject Place(GameObject parent, string name, Vector3 position, Vector3 euler = default, float scale = 1f, float? groundY = null,
                                       SurfaceType surface = SurfaceType.Metal, bool collider = true, bool isStatic = true, float emission = 0f, string instanceName = null, Color? tint = null)
        {
            var asset = Load(name);
            if (asset == null) { if (!Missing.Contains(name)) Missing.Add(name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = instanceName ?? name;
            go.transform.SetParent(parent.transform, false);
            // Single-mesh FBX files carry their axis/unit compensation on the root (e.g. rotation -90 X, scale 100); compose our
            // placement on top of it instead of overwriting it, otherwise such props lie on their back or vanish.
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler) * asset.transform.localRotation;
            go.transform.localScale = asset.transform.localScale * scale;
            var mat = MaterialFor(name, emission, tint);
            int layer = EditorRefs.Layer("Environment");
            Bounds? bounds = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)]; for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                bounds = bounds.HasValue ? Encapsulate(bounds.Value, r.bounds) : r.bounds;
            }
            // Some FBX exports carry a unit scale that Unity's file-scale import turns into a 100x (or 0.01x) object; snap back using the published dimensions.
            var expected = ExpectedSize(name);
            if (expected.HasValue && bounds.HasValue)
            {
                float expectedMax = Mathf.Max(expected.Value.x, expected.Value.y, expected.Value.z);
                float actualMax = Mathf.Max(bounds.Value.size.x, bounds.Value.size.y, bounds.Value.size.z) / Mathf.Max(1e-4f, scale);
                // (the import root scale is already part of the measured bounds)
                if (expectedMax > 0.01f && actualMax > 1e-5f)
                {
                    float ratio = expectedMax / actualMax;
                    if (ratio > 1.8f || ratio < 0.55f)
                    {
                        // unit mistakes are powers of 100; anything else is a genuine size mismatch and is corrected exactly
                        float snapped = (ratio > 50f && ratio < 200f) ? 100f : (ratio > 0.005f && ratio < 0.02f) ? 0.01f : ratio;
                        go.transform.localScale = go.transform.localScale * snapped;
                        bounds = BoundsOf(go);
                        SetupLog.Info($"Prop '{name}' imported {ratio:0.##}x too {(ratio > 1 ? "small" : "large")}; corrected scale x{snapped}");
                    }
                }
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                if (isStatic) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
            if (groundY.HasValue && bounds.HasValue)
            {
                float dy = groundY.Value - bounds.Value.min.y;
                go.transform.position += new Vector3(0f, dy, 0f);
                var b = bounds.Value; b.center += new Vector3(0f, dy, 0f); bounds = b;
            }
            if (collider && bounds.HasValue)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(bounds.Value.center);
                var size = bounds.Value.size; var ls = go.transform.lossyScale;
                // bounds are world-aligned; for yaw-only placements this is a good fit, otherwise it is a generous hull
                bc.size = new Vector3(size.x / Mathf.Max(1e-4f, ls.x), size.y / Mathf.Max(1e-4f, ls.y), size.z / Mathf.Max(1e-4f, ls.z));
                var q = Quaternion.Inverse(go.transform.rotation);
                var rotated = q * size; bc.size = new Vector3(Mathf.Abs(rotated.x) / Mathf.Max(1e-4f, ls.x), Mathf.Abs(rotated.y) / Mathf.Max(1e-4f, ls.y), Mathf.Abs(rotated.z) / Mathf.Max(1e-4f, ls.z));
            }
            EditorRefs.Ensure<SurfaceMaterialTag>(go).surface = surface;
            Placed++;
            return go;
        }

        static Bounds Encapsulate(Bounds a, Bounds b) { a.Encapsulate(b); return a; }

        /// <summary>World-space renderer bounds of a placed prop (null when it has no renderers).</summary>
        public static Bounds? BoundsOf(GameObject go)
        {
            if (go == null) return null;
            Bounds? b = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) b = b.HasValue ? Encapsulate(b.Value, r.bounds) : r.bounds;
            return b;
        }
    }
}
