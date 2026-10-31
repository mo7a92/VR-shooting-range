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
        public const string Version = "0.3.0";
        public const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        public static readonly string[] RequiredLayers = { "Weapon", "Projectile", "Target", "Environment", "HeadsetOnly" };
        /// <summary>Objects only the headset should see (tunnelling vignette); the spectator camera culls this layer.</summary>
        public const string HeadsetOnlyLayer = "HeadsetOnly";
        public const string QuestQualityName = "Quest 2";
        public const string PcvrQualityName = "PCVR";
        public const int QuestQualityIndex = 0;
        public const int PcvrQualityIndex = 1;
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
            PlayerSettings.Android.bundleVersionCode = Mathf.Max(3, PlayerSettings.Android.bundleVersionCode);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.MTRendering = true;
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.gpuSkinning = true;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            SetupLog.Ok("Player settings: Android ARM64, IL2CPP, Vulkan, Linear colour, ASTC, min API 29");
            ApplyStandalonePlayerSettings();
        }

        /// <summary>Windows x64 PCVR player: Mono (buildable from any editor host), D3D11 (the OpenXR path every PC runtime supports),
        /// windowed 1920x1080 desktop mirror for recording, keeps running while unfocused.</summary>
        public static void ApplyStandalonePlayerSettings()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, PackageId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = true;
            PlayerSettings.usePlayerLog = true;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            SetupLog.Ok("Player settings (Windows PCVR): x64, Mono, Direct3D11, windowed 1920x1080 desktop mirror, run in background");
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

        /// <summary>
        /// Two quality levels with their own URP assets: "Quest 2" (index 0, default for Android) and "PCVR" (index 1,
        /// default for Standalone, excluded from Android builds so its shader variants never ship on the headset).
        /// Written through the QualitySettings asset so the editor UI and builds agree.
        /// </summary>
        public static void ApplyQuality()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (assets == null || assets.Length == 0) { SetupLog.Warn("QualitySettings.asset not found"); return; }
            var so = new SerializedObject(assets[0]);
            var levels = so.FindProperty("m_QualitySettings");
            if (levels == null || !levels.isArray) { SetupLog.Warn("m_QualitySettings not found"); return; }
            if (levels.arraySize < 2) levels.arraySize = 2;
            if (levels.arraySize > 2) levels.arraySize = 2;
            ConfigureLevel(levels.GetArrayElementAtIndex(QuestQualityIndex), QuestQualityName, pixelLights: 2, shadows: 1 /*hard*/, shadowRes: 1 /*medium*/, shadowDistance: 18f, cascades: 1, aniso: 1 /*per texture*/, msaa: 4, lodBias: 1f, softParticles: false, realtimeProbes: false);
            ConfigureLevel(levels.GetArrayElementAtIndex(PcvrQualityIndex), PcvrQualityName, pixelLights: 8, shadows: 2 /*all*/, shadowRes: 3 /*very high*/, shadowDistance: 45f, cascades: 4, aniso: 2 /*forced on*/, msaa: 4, lodBias: 2f, softParticles: true, realtimeProbes: true);
            // per-platform defaults
            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            if (map != null && map.isArray)
            {
                bool hasAndroid = false, hasStandalone = false;
                for (int i = 0; i < map.arraySize; i++)
                {
                    var el = map.GetArrayElementAtIndex(i); var key = el.FindPropertyRelative("first"); var val = el.FindPropertyRelative("second");
                    if (key == null || val == null) continue;
                    if (key.stringValue == "Android") { val.intValue = QuestQualityIndex; hasAndroid = true; }
                    else if (key.stringValue == "Standalone") { val.intValue = PcvrQualityIndex; hasStandalone = true; }
                    else if (val.intValue > 1) val.intValue = QuestQualityIndex;
                }
                if (!hasAndroid) { map.arraySize++; var el = map.GetArrayElementAtIndex(map.arraySize - 1); el.FindPropertyRelative("first").stringValue = "Android"; el.FindPropertyRelative("second").intValue = QuestQualityIndex; }
                if (!hasStandalone) { map.arraySize++; var el = map.GetArrayElementAtIndex(map.arraySize - 1); el.FindPropertyRelative("first").stringValue = "Standalone"; el.FindPropertyRelative("second").intValue = PcvrQualityIndex; }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            // Keep the PCVR level (and its URP asset) out of Android builds; the Quest level stays available everywhere for comparison.
            if (!QualitySettings.TryExcludePlatformAt("Android", PcvrQualityIndex, out var err) && err != null && !(err is System.InvalidOperationException)) SetupLog.Warn($"Could not exclude Android from the PCVR quality level: {err.Message}");
            QualitySettings.vSyncCount = 0; // the XR runtime paces frames
            SetupLog.Ok($"Quality levels: [{QuestQualityIndex}] {QuestQualityName} (Android default), [{PcvrQualityIndex}] {PcvrQualityName} (Standalone default, excluded from Android)");
        }

        static void ConfigureLevel(SerializedProperty level, string name, int pixelLights, int shadows, int shadowRes, float shadowDistance, int cascades, int aniso, int msaa, float lodBias, bool softParticles, bool realtimeProbes)
        {
            void SetI(string n, int v) { var p = level.FindPropertyRelative(n); if (p != null) p.intValue = v; }
            void SetF(string n, float v) { var p = level.FindPropertyRelative(n); if (p != null) p.floatValue = v; }
            void SetB(string n, bool v) { var p = level.FindPropertyRelative(n); if (p != null) p.boolValue = v; }
            var nameProp = level.FindPropertyRelative("name"); if (nameProp != null) nameProp.stringValue = name;
            SetI("pixelLightCount", pixelLights); SetI("shadows", shadows); SetI("shadowResolution", shadowRes); SetI("shadowProjection", 1);
            SetI("shadowCascades", cascades); SetF("shadowDistance", shadowDistance); SetF("shadowNearPlaneOffset", 3f); SetI("shadowmaskMode", 0);
            SetI("skinWeights", 4); SetI("globalTextureMipmapLimit", 0); SetI("anisotropicTextures", aniso); SetI("antiAliasing", msaa);
            SetB("softParticles", softParticles); SetB("softVegetation", true); SetB("realtimeReflectionProbes", realtimeProbes); SetB("billboardsFaceCameraPosition", true);
            SetI("vSyncCount", 0); SetF("lodBias", lodBias); SetI("maximumLODLevel", 0); SetB("enableLODCrossFade", true);
            SetB("streamingMipmapsActive", false); SetI("particleRaycastBudget", 256); SetI("asyncUploadTimeSlice", 2); SetI("asyncUploadBufferSize", 16);
            var excluded = level.FindPropertyRelative("excludedTargetPlatforms"); if (excluded != null && excluded.isArray && name == QuestQualityName) excluded.arraySize = 0;
        }
    }
}
