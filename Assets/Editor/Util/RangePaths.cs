using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Central list of generated asset locations so every builder agrees on paths.</summary>
    public static class RangePaths
    {
        public const string ArtTextures = "Assets/Art/Textures";
        public const string ArtSigns = "Assets/Art/Signs";
        public const string ArtFx = "Assets/Art/FX";
        public const string ArtWeapons = "Assets/Art/Weapons";
        public const string Audio = "Assets/Audio";
        public const string Generated = "Assets/Generated";
        public const string Materials = "Assets/Generated/Materials";
        public const string Meshes = "Assets/Generated/Meshes";
        public const string Prefabs = "Assets/Generated/Prefabs";
        public const string Settings = "Assets/Settings";
        public const string Scenes = "Assets/Scenes";
        public const string MainScene = "Assets/Scenes/ShootingRange.unity";
        public const string UrpAsset = "Assets/Settings/URP-Quest2.asset";
        public const string UrpRenderer = "Assets/Settings/URP-Quest2-Renderer.asset";
        public const string LightingSettings = "Assets/Settings/RangeLightingSettings.lighting";
        public const string StarterAssets = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets";
        public const string XrRigPrefab = StarterAssets + "/Prefabs/XR Origin (XR Rig).prefab";
        public const string TunnelingVignettePrefab = StarterAssets + "/TunnelingVignette/TunnelingVignette.prefab";
        public const string BootstrapState = "ProjectSettings/VRRangeBootstrapState.json";
        public const string ReportFile = "Logs/VRRangeSetupReport.md";

        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            var leaf = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static void EnsureAllFolders()
        {
            foreach (var p in new[] { Generated, Materials, Meshes, Prefabs, Settings, Scenes }) EnsureFolder(p);
        }

        public static T LoadOrNull<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        public static T LoadOrCreate<T>(string path, System.Action<T> init = null) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            a = ScriptableObject.CreateInstance<T>();
            init?.Invoke(a);
            AssetDatabase.CreateAsset(a, path);
            return a;
        }
    }
}
