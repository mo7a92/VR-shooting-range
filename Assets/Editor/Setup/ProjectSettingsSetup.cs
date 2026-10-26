using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;

namespace VRRange.Editor
{
    /// <summary>
    /// Idempotent player / XR Plug-in Management / layer configuration for a Meta Quest 2 standalone build.
    /// Every call re-applies the same values; nothing is duplicated.
    /// </summary>
    public static class ProjectSettingsSetup
    {
        public const string ProductName = "VR Shooting Range";
        public const string CompanyName = "UniversityProject";
        public const string PackageId = "com.universityproject.vrshootingrange";
        public const string Version = "0.1.0";
        public const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        public static readonly string[] RequiredLayers = { "Weapon", "Projectile", "Target", "Environment" };
        public const string TeleportInteractionLayer = "Teleport";
        public const int TeleportInteractionLayerIndex = 31;

        public static void ApplyAll()
        {
            SetupLog.Section("Project settings");
            ApplyPlayerSettings();
            ApplyLayers();
            ApplyInteractionLayers();
            ApplyInputHandler();
            ApplyXRManagement();
            ApplyQuality();
            AssetDatabase.SaveAssets();
        }

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29; // Android 10 (Quest 2 ships Android 10+)
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.Android.androidTVCompatibility = false;
            PlayerSettings.Android.optimizedFramePacing = false; // XR runtime paces frames
            PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.MTRendering = true;
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.gpuSkinning = true;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            SetupLog.Ok("Player settings: Android ARM64, IL2CPP, Vulkan, Linear colour, ASTC, min API 29");
        }

        /// <summary>Adds the physics layers the project uses (first free slots from 8 upward).</summary>
        public static void ApplyLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) { SetupLog.Warn("TagManager.asset not found; layers not created"); return; }
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            foreach (var name in RequiredLayers)
            {
                bool exists = false;
                for (int i = 8; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == name) { exists = true; break; }
                if (exists) continue;
                bool placed = false;
                for (int i = 8; i < layers.arraySize; i++)
                {
                    var el = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = name; placed = true; break; }
                }
                if (!placed) SetupLog.Warn($"No free layer slot for '{name}'");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            // Projectiles ignore weapons and other projectiles at the physics level too.
            int lw = LayerMask.NameToLayer("Weapon"), lp = LayerMask.NameToLayer("Projectile");
            if (lw >= 0 && lp >= 0) { Physics.IgnoreLayerCollision(lp, lw, true); Physics.IgnoreLayerCollision(lp, lp, true); }
            SetupLog.Ok("Physics layers: " + string.Join(", ", RequiredLayers));
        }

        /// <summary>
        /// XRI interaction layer 31 = "Teleport" (what the Starter Assets rig expects). InteractionLayerSettings is
        /// internal to XRI, so the asset is reached through its public ScriptableSettings base via reflection and
        /// edited with a SerializedObject (field m_LayerNames, verified in XRI 3.3.2 source).
        /// </summary>
        public static void ApplyInteractionLayers()
        {
            try
            {
                var type = typeof(XRInteractionManager).Assembly.GetType("UnityEngine.XR.Interaction.Toolkit.InteractionLayerSettings");
                var instanceProp = type?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
                var settings = instanceProp?.GetValue(null) as ScriptableObject;
                if (settings == null) { SetupLog.Warn("InteractionLayerSettings unavailable; set Interaction Layer 31 to 'Teleport' in Project Settings > XR Interaction Toolkit."); return; }
                var so = new SerializedObject(settings);
                var names = so.FindProperty("m_LayerNames");
                if (names == null || !names.isArray) { SetupLog.Warn("InteractionLayerSettings.m_LayerNames not found"); return; }
                if (names.arraySize <= TeleportInteractionLayerIndex) names.arraySize = TeleportInteractionLayerIndex + 1;
                var el = names.GetArrayElementAtIndex(TeleportInteractionLayerIndex);
                if (el.stringValue != TeleportInteractionLayer) { el.stringValue = TeleportInteractionLayer; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); }
                SetupLog.Ok($"XRI interaction layer {TeleportInteractionLayerIndex} = {TeleportInteractionLayer}");
            }
            catch (System.Exception e) { SetupLog.Warn($"Could not set XRI interaction layer: {e.Message}"); }
        }

        /// <summary>Active Input Handling = Input System only (value 1). Requires an editor restart when changed.</summary>
        public static bool ApplyInputHandler()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { SetupLog.Warn("ProjectSettings.asset not found"); return false; }
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null) { SetupLog.Warn("activeInputHandler property not found"); return false; }
            if (prop.intValue == 1) { SetupLog.Ok("Active Input Handling already = Input System"); return false; }
            prop.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetupLog.Warn("Active Input Handling switched to Input System: Unity needs a restart (close and reopen the project) for it to take effect.");
            return true;
        }

        /// <summary>Enables the OpenXR loader for Android (and Standalone for in-editor testing) via XR Plug-in Management.</summary>
        public static void ApplyXRManagement()
        {
            XRGeneralSettingsPerBuildTarget buildTargetSettings = null;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out buildTargetSettings);
            if (buildTargetSettings == null)
            {
                RangePaths.EnsureFolder("Assets/XR");
                buildTargetSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(buildTargetSettings, "Assets/XR/XRGeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, buildTargetSettings, true);
                SetupLog.Info("Created XRGeneralSettings asset");
            }
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!buildTargetSettings.HasSettingsForBuildTarget(group))
                    buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(group);
                var settings = buildTargetSettings.SettingsForBuildTarget(group);
                if (settings == null) { SetupLog.Warn($"Could not create XR settings for {group}"); continue; }
                settings.InitManagerOnStart = true;
                var manager = settings.AssignedSettings;
                if (manager == null) { SetupLog.Warn($"No XRManagerSettings for {group}"); continue; }
                if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderTypeName, group))
                {
                    if (XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderTypeName, group))
                        SetupLog.Ok($"OpenXR loader enabled for {group}");
                    else
                        SetupLog.Warn($"Could not assign the OpenXR loader for {group}. Open Project Settings > XR Plug-in Management and tick OpenXR.");
                }
                else SetupLog.Ok($"OpenXR loader already enabled for {group}");
                EditorUtility.SetDirty(settings); EditorUtility.SetDirty(manager);
            }
            EditorUtility.SetDirty(buildTargetSettings);
        }

        public static void ApplyQuality()
        {
            // Mobile VR: no soft particles, 4x MSAA via URP asset, VSync handled by the XR runtime.
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.shadowDistance = 20f;
            QualitySettings.lodBias = 1f;
            QualitySettings.globalTextureMipmapLimit = 0;
            SetupLog.Ok("Quality settings tuned for Quest 2");
        }
    }
}
