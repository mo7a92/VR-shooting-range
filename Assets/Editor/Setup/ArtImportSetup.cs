using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Applies Quest-friendly import settings to the generated textures, meshes and audio.</summary>
    public static class ArtImportSetup
    {
        public static void Apply()
        {
            SetupLog.Section("Art import settings");
            int textures = 0, audio = 0, models = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RangePaths.ArtTextures, RangePaths.ArtSigns, RangePaths.ArtFx }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && ConfigureTexture(ti, path)) { ti.SaveAndReimport(); textures++; }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { RangePaths.Audio }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is AudioImporter ai && ConfigureAudio(ai, path)) { ai.SaveAndReimport(); audio++; }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { RangePaths.ArtWeapons }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && ConfigureModel(mi)) { mi.SaveAndReimport(); models++; }
            }
            SetupLog.Ok($"Import settings applied: {textures} textures, {audio} audio clips, {models} models changed");
        }

        static bool ConfigureTexture(TextureImporter ti, string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            bool changed = false;
            void Set<T>(ref T field, T value) { if (!Equals(field, value)) { field = value; changed = true; } }

            bool isNormal = name.EndsWith("_normal");
            bool isMask = name.EndsWith("_mask");
            bool isSprite = path.StartsWith(RangePaths.ArtFx);
            var type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type) { ti.textureType = type; changed = true; }
            if (ti.sRGBTexture != (!isNormal && !isMask)) { ti.sRGBTexture = !isNormal && !isMask; changed = true; }
            bool alpha = isMask || isSprite;
            if (ti.alphaIsTransparency != (isSprite && !isMask)) { ti.alphaIsTransparency = isSprite && !isMask; changed = true; }
            if (ti.mipmapEnabled != true) { ti.mipmapEnabled = true; changed = true; }
            if (ti.wrapMode != (isSprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat)) { ti.wrapMode = isSprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; changed = true; }
            if (ti.anisoLevel != 4) { ti.anisoLevel = 4; changed = true; }
            int maxSize = isSprite ? 256 : (path.StartsWith(RangePaths.ArtSigns) ? 1024 : 1024);
            var android = ti.GetPlatformTextureSettings("Android");
            var fmt = alpha ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_6x6;
            if (!android.overridden || android.maxTextureSize != maxSize || android.format != fmt)
            {
                android.overridden = true; android.maxTextureSize = maxSize; android.format = fmt; android.compressionQuality = 50;
                ti.SetPlatformTextureSettings(android); changed = true;
            }
            if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; changed = true; }
            return changed;
        }

        static bool ConfigureAudio(AudioImporter ai, string path)
        {
            var s = ai.defaultSampleSettings;
            bool loop = path.Contains("Ambience");
            var wanted = new AudioImporterSampleSettings
            {
                loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = loop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM,
                quality = 0.6f, sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate
            };
            if (s.loadType == wanted.loadType && s.compressionFormat == wanted.compressionFormat && ai.forceToMono) return false;
            ai.forceToMono = true;
            ai.defaultSampleSettings = wanted;
            return true;
        }

        static bool ConfigureModel(ModelImporter mi)
        {
            bool changed = false;
            if (mi.globalScale != 1f) { mi.globalScale = 1f; changed = true; }
            if (mi.useFileScale != false) { mi.useFileScale = false; changed = true; }
            if (mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; changed = true; }
            if (mi.importTangents != ModelImporterTangents.CalculateMikk) { mi.importTangents = ModelImporterTangents.CalculateMikk; changed = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (mi.importAnimation) { mi.importAnimation = false; changed = true; }
            if (mi.meshCompression != ModelImporterMeshCompression.Low) { mi.meshCompression = ModelImporterMeshCompression.Low; changed = true; }
            if (!mi.isReadable) { mi.isReadable = true; changed = true; } // builder reads submesh material names
            return changed;
        }
    }
}
