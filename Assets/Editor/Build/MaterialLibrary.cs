using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Creates (or updates) URP Lit materials from the generated texture sets. Idempotent by asset path.</summary>
    public static class MaterialLibrary
    {
        static Shader s_Lit, s_Unlit, s_Particle;
        static readonly Dictionary<string, Material> s_Cache = new Dictionary<string, Material>();

        public static Shader Lit => s_Lit != null ? s_Lit : (s_Lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        public static Shader Unlit => s_Unlit != null ? s_Unlit : (s_Unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
        public static Shader Particle => s_Particle != null ? s_Particle : (s_Particle = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit"));

        public static void ResetCache() => s_Cache.Clear();

        /// <summary>PBR material from &lt;name&gt;_albedo/_normal/_mask textures in Assets/Art/Textures.</summary>
        public static Material Pbr(string textureSet, string materialName = null, float tiling = 1f, float smoothnessFallback = 0.4f, Color? tint = null)
        {
            materialName ??= textureSet;
            var path = $"{RangePaths.Materials}/{materialName}.mat";
            if (s_Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) { mat = new Material(Lit); RangePaths.EnsureFolder(RangePaths.Materials); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Lit;
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtTextures}/{textureSet}_albedo.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtTextures}/{textureSet}_normal.png");
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtTextures}/{textureSet}_mask.png");
            if (albedo != null) mat.SetTexture("_BaseMap", albedo); else SetupLog.Warn($"Texture missing: {textureSet}_albedo.png (run Tools/AssetGen/gen_textures.py)");
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            if (mask != null) { mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 1f); }
            else mat.SetFloat("_Smoothness", smoothnessFallback);
            mat.SetColor("_BaseColor", tint ?? Color.white);
            mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            s_Cache[path] = mat;
            return mat;
        }

        public static Material Flat(string materialName, Color color, float smoothness = 0.3f, float metallic = 0f, Color? emission = null)
        {
            var path = $"{RangePaths.Materials}/{materialName}.mat";
            if (s_Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Lit); RangePaths.EnsureFolder(RangePaths.Materials); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Lit;
            mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", smoothness); mat.SetFloat("_Metallic", metallic);
            if (emission.HasValue) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", emission.Value); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            s_Cache[path] = mat;
            return mat;
        }

        /// <summary>Unlit textured material (signs, target faces, light panels). Optional transparency for decals.</summary>
        public static Material UnlitTexture(string materialName, Texture2D texture, bool transparent = false, bool lit = true, Color? emission = null)
        {
            var path = $"{RangePaths.Materials}/{materialName}.mat";
            if (s_Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = lit ? Lit : Unlit;
            if (mat == null) { mat = new Material(shader); RangePaths.EnsureFolder(RangePaths.Materials); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            if (texture != null) mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            if (lit) mat.SetFloat("_Smoothness", 0.2f);
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f); mat.SetFloat("_ZWrite", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (emission.HasValue) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", emission.Value); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            s_Cache[path] = mat;
            return mat;
        }

        public static Material ParticleAdditive(string materialName, Texture2D texture)
        {
            var path = $"{RangePaths.Materials}/{materialName}.mat";
            if (s_Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Particle); RangePaths.EnsureFolder(RangePaths.Materials); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = Particle;
            if (texture != null) mat.SetTexture("_BaseMap", texture);
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 2f); // additive
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            s_Cache[path] = mat;
            return mat;
        }
    }
}
