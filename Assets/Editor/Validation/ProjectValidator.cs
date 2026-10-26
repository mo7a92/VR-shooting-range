using UnityEditor;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

namespace VRRange.Editor
{
    /// <summary>Reports what is in place and what still needs attention. Never changes anything.</summary>
    public static class ProjectValidator
    {
        [MenuItem("VR Range/Validate Project", priority = 60)]
        public static void RunMenu()
        {
            SetupLog.Begin("VR Range validation");
            Run();
            var report = SetupLog.Write();
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("VR Range validation", SetupLog.ProblemCount == 0 ? "All checks passed." : $"{SetupLog.ProblemCount} item(s) need attention. See {RangePaths.ReportFile}", "OK");
            Debug.Log(report);
        }

        public static void Run()
        {
            SetupLog.Section("Validation");
            Check(AssetDatabase.IsValidFolder("Packages/com.unity.xr.interaction.toolkit"), "XR Interaction Toolkit package present", "XR Interaction Toolkit missing (check Packages/manifest.json)");
            Check(AssetDatabase.IsValidFolder("Packages/com.unity.xr.openxr"), "OpenXR package present", "OpenXR package missing");
            Check(AssetDatabase.IsValidFolder("Packages/com.unity.render-pipelines.universal"), "URP package present", "URP package missing");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(RangePaths.XrRigPrefab) != null, "XRI Starter Assets rig prefab found", "XRI Starter Assets sample missing: import it from Package Manager > XR Interaction Toolkit > Samples");
            Check(RangeSceneBuilder.SceneExists, "ShootingRange scene exists", "ShootingRange scene not generated");
            foreach (var p in new[] { "Weapon_Pistol", "Weapon_Rifle", "Weapon_SMG", "Target_Bullseye", "Projectile" })
                Check(AssetDatabase.LoadAssetAtPath<GameObject>($"{RangePaths.Prefabs}/{p}.prefab") != null, $"Prefab {p} exists", $"Prefab {p} missing");
            Check(AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangePaths.ArtTextures}/target_face_albedo.png") != null, "Generated textures present", "Generated textures missing (run Tools/AssetGen/gen_textures.py)");
            Check(AssetDatabase.LoadAssetAtPath<AudioClip>($"{RangePaths.Audio}/Weapons/pistol_fire.wav") != null, "Generated audio present", "Generated audio missing (run Tools/AssetGen/gen_audio.py)");
            Check(AssetDatabase.LoadAssetAtPath<Mesh>($"{RangePaths.ArtWeapons}/pistol_body.obj") != null, "Weapon meshes present", "Weapon meshes missing (run Tools/AssetGen/gen_weapons.py)");
            Check(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null, "A render pipeline asset is assigned", "No URP asset assigned (Graphics settings)");
            Check(XRPackageMetadataStore.IsLoaderAssigned(ProjectSettingsSetup.OpenXRLoaderTypeName, BuildTargetGroup.Android), "OpenXR loader enabled for Android", "OpenXR loader not enabled for Android (Project Settings > XR Plug-in Management > Android)");
            OpenXRSettingsSetup.ValidateFeatures();
            Check(PlayerSettings.GetScriptingBackend(NamedBuildTargetAndroid()) == ScriptingImplementation.IL2CPP, "IL2CPP scripting backend", "Scripting backend is not IL2CPP");
            Check(PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64, "ARM64 architecture", "Target architecture is not ARM64");
            Check(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android), "Android Build Support installed", "Android Build Support module not installed in this editor (Unity Hub > Installs > Add modules)");
            Check(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android, "Active build target is Android", "Active build target is not Android (File > Build Profiles > Android > Switch Platform)");
            Check(TMPro.TMP_Settings.instance != null, "TMP Essential Resources imported", "TMP Essential Resources not imported (UI uses fallback font); Window > TextMeshPro > Import TMP Essential Resources");
            Check(Lightmapping.lightingSettings != null, "Lighting settings assigned", "No lighting settings asset assigned");
            SetupLog.Info("Lightmaps are NOT baked by setup; run VR Range > Build > Bake Lighting before the final build for best Quest 2 performance.");
        }

        static UnityEditor.Build.NamedBuildTarget NamedBuildTargetAndroid() => UnityEditor.Build.NamedBuildTarget.Android;

        public static void Check(bool ok, string okMsg, string failMsg) { if (ok) SetupLog.Ok(okMsg); else SetupLog.Warn(failMsg); }
    }
}
