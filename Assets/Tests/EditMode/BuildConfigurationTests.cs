using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using VRRange.Editor;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>Build configuration for both platforms: XR/OpenXR setup, interaction profiles, quality presets, player settings, input bindings.</summary>
    public class BuildConfigurationTests
    {
        [Test]
        public void OpenXRLoaderIsEnabledForAndroidAndStandalone()
        {
            Assert.IsTrue(XRPackageMetadataStore.IsLoaderAssigned(ProjectSettingsSetup.OpenXRLoaderTypeName, BuildTargetGroup.Android), "Android (Quest 2)");
            Assert.IsTrue(XRPackageMetadataStore.IsLoaderAssigned(ProjectSettingsSetup.OpenXRLoaderTypeName, BuildTargetGroup.Standalone), "Standalone (Windows PCVR)");
        }

        [Test]
        public void InteractionProfilesCoverQuestAndPcHeadsets()
        {
            var android = OpenXRSettingsSetup.EnabledProfileIds(BuildTargetGroup.Android);
            CollectionAssert.Contains(android, OculusTouchControllerProfile.featureId, "Quest 2 Touch controllers");
            var pc = OpenXRSettingsSetup.EnabledProfileIds(BuildTargetGroup.Standalone);
            foreach (var id in new[] { OculusTouchControllerProfile.featureId, ValveIndexControllerProfile.featureId, HTCViveControllerProfile.featureId, MetaQuestTouchProControllerProfile.featureId })
                CollectionAssert.Contains(pc, id, $"PC profile {id}");
            var pcSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.IsNotNull(pcSettings);
            Assert.AreEqual(OpenXRSettings.RenderMode.SinglePassInstanced, pcSettings.renderMode);
            Assert.AreEqual(OpenXRSettings.DepthSubmissionMode.Depth16Bit, pcSettings.depthSubmissionMode, "depth submission helps PC runtime reprojection");
            var androidSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Assert.AreEqual(OpenXRSettings.RenderMode.SinglePassInstanced, androidSettings.renderMode);
            var quest = androidSettings.GetFeature<UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature>();
            Assert.IsTrue(quest != null && quest.enabled, "Meta Quest Support feature");
        }

        [Test]
        public void QualityLevelsAreSplitPerPlatform()
        {
            var names = QualitySettings.names;
            Assert.GreaterOrEqual(names.Length, 2);
            Assert.AreEqual(ProjectSettingsSetup.QuestQualityName, names[ProjectSettingsSetup.QuestQualityIndex]);
            Assert.AreEqual(ProjectSettingsSetup.PcvrQualityName, names[ProjectSettingsSetup.PcvrQualityIndex]);
            var androidLevels = QualitySettings.GetActiveQualityLevelsForPlatform("Android");
            CollectionAssert.Contains(androidLevels, ProjectSettingsSetup.QuestQualityIndex);
            CollectionAssert.DoesNotContain(androidLevels, ProjectSettingsSetup.PcvrQualityIndex, "PCVR preset must not ship in the Quest build");
            var standaloneLevels = QualitySettings.GetActiveQualityLevelsForPlatform("Standalone");
            CollectionAssert.Contains(standaloneLevels, ProjectSettingsSetup.PcvrQualityIndex);
            // defaults
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            int android = -1, standalone = -1;
            for (int i = 0; i < map.arraySize; i++) { var el = map.GetArrayElementAtIndex(i); var k = el.FindPropertyRelative("first").stringValue; var v = el.FindPropertyRelative("second").intValue; if (k == "Android") android = v; if (k == "Standalone") standalone = v; }
            Assert.AreEqual(ProjectSettingsSetup.QuestQualityIndex, android, "Android defaults to Quest 2");
            Assert.AreEqual(ProjectSettingsSetup.PcvrQualityIndex, standalone, "Standalone defaults to PCVR");
        }

        [Test]
        public void UrpPresetsDifferAsIntended()
        {
            var quest = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RangePaths.UrpAsset);
            var pcvr = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RangePaths.UrpPcvrAsset);
            Assert.IsNotNull(quest); Assert.IsNotNull(pcvr);
            Assert.AreSame(quest, GraphicsSettings.defaultRenderPipeline, "Quest preset is the fallback pipeline");
            Assert.IsFalse(quest.supportsHDR); Assert.IsTrue(pcvr.supportsHDR);
            Assert.AreEqual(4, quest.msaaSampleCount); Assert.AreEqual(4, pcvr.msaaSampleCount);
            Assert.AreEqual(1, quest.shadowCascadeCount); Assert.AreEqual(4, pcvr.shadowCascadeCount);
            Assert.Greater(pcvr.mainLightShadowmapResolution, quest.mainLightShadowmapResolution);
            Assert.Greater(pcvr.shadowDistance, quest.shadowDistance);
            Assert.IsFalse(quest.supportsCameraDepthTexture); Assert.IsTrue(pcvr.supportsCameraDepthTexture, "SSAO needs depth");
            Assert.IsFalse(quest.supportsAdditionalLightShadows); Assert.IsTrue(pcvr.supportsAdditionalLightShadows);
            Assert.AreEqual(LightRenderingMode.PerVertex, quest.additionalLightsRenderingMode); Assert.AreEqual(LightRenderingMode.PerPixel, pcvr.additionalLightsRenderingMode);
            var questRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RangePaths.UrpRenderer);
            var pcvrRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RangePaths.UrpPcvrRenderer);
            Assert.IsFalse(questRenderer.rendererFeatures.Any(f => f is ScreenSpaceAmbientOcclusion), "no SSAO on Quest");
            Assert.IsTrue(pcvrRenderer.rendererFeatures.Any(f => f is ScreenSpaceAmbientOcclusion && f.isActive), "SSAO on PCVR");
            Assert.IsFalse(pcvrRenderer.rendererFeatures.Contains(null), "no broken renderer feature entries");
            // the quality levels point at the right assets
            int current = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(ProjectSettingsSetup.QuestQualityIndex, false); Assert.AreSame(quest, QualitySettings.renderPipeline);
            QualitySettings.SetQualityLevel(ProjectSettingsSetup.PcvrQualityIndex, false); Assert.AreSame(pcvr, QualitySettings.renderPipeline);
            QualitySettings.SetQualityLevel(current, false);
        }

        [Test]
        public void PlayerSettingsMatchBothTargets()
        {
            Assert.AreEqual(ScriptingImplementation.IL2CPP, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android));
            Assert.AreEqual(AndroidArchitecture.ARM64, PlayerSettings.Android.targetArchitectures);
            CollectionAssert.AreEqual(new[] { GraphicsDeviceType.Vulkan }, PlayerSettings.GetGraphicsAPIs(BuildTarget.Android));
            Assert.AreEqual(ScriptingImplementation.Mono2x, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone), "Windows player builds with Mono from any editor host");
            CollectionAssert.AreEqual(new[] { GraphicsDeviceType.Direct3D11 }, PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64));
            Assert.IsTrue(PlayerSettings.runInBackground, "keeps rendering when the desktop window loses focus");
            Assert.AreEqual(FullScreenMode.Windowed, PlayerSettings.fullScreenMode);
            Assert.AreEqual(1920, PlayerSettings.defaultScreenWidth); Assert.AreEqual(1080, PlayerSettings.defaultScreenHeight);
            Assert.IsTrue(PlayerSettings.resizableWindow);
            Assert.AreEqual(ColorSpace.Linear, PlayerSettings.colorSpace);
            Assert.AreEqual(ProjectSettingsSetup.PackageId, PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
            Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == RangePaths.MainScene), "scene in build settings");
            foreach (var layer in ProjectSettingsSetup.RequiredLayers) Assert.GreaterOrEqual(LayerMask.NameToLayer(layer), 8, $"layer {layer}");
            int lp = LayerMask.NameToLayer("Projectile"), lw = LayerMask.NameToLayer("Weapon");
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(lp, lw), "projectile/weapon physics collisions ignored");
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(lp, lp));
        }

        [Test]
        public void WeaponExtraButtonsHaveFallbackBindingsForAllControllerFamilies()
        {
            var paths = WeaponInput.BindingPaths();
            CollectionAssert.Contains(paths, "<XRController>{RightHand}/primaryButton");
            CollectionAssert.Contains(paths, "<XRController>{LeftHand}/secondaryButton");
            CollectionAssert.Contains(paths, "<XRController>{RightHand}/trackpadClicked", "Vive wand fallback for reload");
            CollectionAssert.Contains(paths, "<XRController>{LeftHand}/menu", "Vive wand fallback for fire mode");
        }

        [Test]
        public void BuildModulesAreReported()
        {
            // Informational: which targets this editor can build. The Quest APK needs Android support, the PCVR exe Windows support.
            bool android = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
            bool windows = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            Debug.Log($"[VRRange] Build support in this editor: Android={android}, Windows x64={windows}");
            Assert.IsTrue(android || windows, "at least one target must be buildable here");
        }
    }
}
