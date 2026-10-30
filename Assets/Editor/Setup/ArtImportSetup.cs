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
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RangePaths.ArtTextures, RangePaths.ArtSigns, RangePaths.ArtFx, RangePaths.ArtUI }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter ti && ConfigureTexture(ti, path)) { ti.SaveAndReimport(); textures++; }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { RangePaths.Audio }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is AudioImporter ai && ConfigureAudio(ai, path)) { ai.SaveAndReimport(); audio++; }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { RangePaths.ArtWeapons, RangePaths.ArtHands }))
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
            bool isNormal = name.EndsWith("_normal");
            bool isMask = name.EndsWith("_mask") || name.EndsWith("_ao");
            bool isSprite = path.StartsWith(RangePaths.ArtFx);
            bool isUi = path.StartsWith(RangePaths.ArtUI);
            var type = isNormal ? TextureImporterType.NormalMap : (isUi ? TextureImporterType.Sprite : TextureImporterType.Default);
            if (isUi)
            {
                isSprite = true;
                var border = name.StartsWith("ui_") && !name.Contains("shadow") ? new Vector4(20, 20, 20, 20) : (name.Contains("shadow") ? new Vector4(30, 30, 30, 30) : Vector4.zero);
                if (ti.spriteBorder != border) { ti.spriteBorder = border; changed = true; }
                if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
            }
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
            string path = mi.assetPath;
            bool handModel = path.StartsWith(RangePaths.ArtHands);
            if (mi.globalScale != 1f) { mi.globalScale = 1f; changed = true; }
            // Our OBJ weapons are authored in metres (ignore file scale); the XR Hands FBX files carry a cm file scale that must be kept.
            if (mi.useFileScale != handModel) { mi.useFileScale = handModel; changed = true; }
            if (mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; changed = true; }
            if (mi.importTangents != ModelImporterTangents.CalculateMikk) { mi.importTangents = ModelImporterTangents.CalculateMikk; changed = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            bool isHand = path.StartsWith(RangePaths.ArtHands);
            if (mi.importAnimation != false) { mi.importAnimation = false; changed = true; }
            if (isHand)
            {
                // keep the skinned rig (joints + SkinnedMeshRenderer); HandPresence rotates the joints directly, no clips needed
                if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; changed = true; }
                if (mi.optimizeGameObjects) { mi.optimizeGameObjects = false; changed = true; }
                if (mi.importBlendShapes) { mi.importBlendShapes = false; changed = true; }
            }
            if (mi.meshCompression != ModelImporterMeshCompression.Low) { mi.meshCompression = ModelImporterMeshCompression.Low; changed = true; }
            if (!mi.isReadable) { mi.isReadable = true; changed = true; } // builder reads submesh material names
            return changed;
        }
    }
}
