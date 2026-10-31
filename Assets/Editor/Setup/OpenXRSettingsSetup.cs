// NOTE: this file is excluded from the cloud compile check (it needs Unity.XR.OpenXR.Editor, which is not part of
// the reference build). Every API below was verified against com.unity.xr.openxr@1.16.1 source by inspection.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace VRRange.Editor
{
    /// <summary>
    /// OpenXR per platform. Android (Quest 2 standalone): Meta Quest Support + Oculus Touch profile. Standalone (Windows
    /// PCVR through Virtual Desktop/VDXR, SteamVR or the Oculus runtime): Oculus Touch, Meta Quest Touch Pro/Plus, Valve
    /// Index, HTC Vive, HP Reverb G2 and Microsoft Motion Controller profiles so every common PC headset binds.
    /// </summary>
    public static class OpenXRSettingsSetup
    {
        public static readonly string[] AndroidProfiles = { OculusTouchControllerProfile.featureId, MetaQuestTouchProControllerProfile.featureId };
        public static readonly string[] StandaloneProfiles =
        {
            OculusTouchControllerProfile.featureId, MetaQuestTouchProControllerProfile.featureId, MetaQuestTouchPlusControllerProfile.featureId,
            ValveIndexControllerProfile.featureId, HTCViveControllerProfile.featureId, HPReverbG2ControllerProfile.featureId, MicrosoftMotionControllerProfile.featureId
        };

        public static void Apply()
        {
            SetupLog.Section("OpenXR");
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                FeatureHelpers.RefreshFeatures(group);
                var wanted = group == BuildTargetGroup.Android ? AndroidProfiles : StandaloneProfiles;
                var enabledNames = new List<string>();
                foreach (var id in wanted)
                {
                    var f = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id);
                    if (f == null) { SetupLog.Warn($"{group}: OpenXR interaction profile '{id}' not found (OpenXR package missing or not yet imported)"); continue; }
                    if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); }
                    enabledNames.Add(f.name);
                }
                SetupLog.Ok($"{group}: interaction profiles enabled: {string.Join(", ", enabledNames)}");

                if (group == BuildTargetGroup.Android)
                {
                    var quest = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, MetaQuestFeature.featureId);
                    if (quest != null) { if (!quest.enabled) { quest.enabled = true; EditorUtility.SetDirty(quest); } SetupLog.Ok("Android: Meta Quest Support feature enabled"); }
                    else SetupLog.Warn("Android: Meta Quest Support feature not found. In Project Settings > XR Plug-in Management > OpenXR, enable 'Meta Quest Support'.");
                }
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings != null)
                {
                    settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                    // PC runtimes (VDXR, SteamVR) use the depth buffer for better reprojection; Quest keeps the cheaper path.
                    settings.depthSubmissionMode = group == BuildTargetGroup.Standalone ? OpenXRSettings.DepthSubmissionMode.Depth16Bit : OpenXRSettings.DepthSubmissionMode.None;
                    EditorUtility.SetDirty(settings);
                    SetupLog.Ok($"{group}: render mode Single Pass Instanced, depth submission {settings.depthSubmissionMode}");
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Validation-only: reports whether the required features/profiles are enabled for both platforms.</summary>
        public static void ValidateFeatures()
        {
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (oxr == null) { SetupLog.Warn("OpenXR settings for Android not found yet (they are created when the OpenXR loader is enabled)"); }
            else
            {
                var quest = oxr.GetFeature<MetaQuestFeature>();
                ProjectValidator.Check(quest != null && quest.enabled, "Android: Meta Quest Support feature enabled", "Meta Quest Support feature not enabled (Project Settings > XR Plug-in Management > OpenXR > Android)");
                var touch = oxr.GetFeature<OculusTouchControllerProfile>();
                ProjectValidator.Check(touch != null && touch.enabled, "Android: Oculus Touch Controller Profile enabled", "Oculus Touch Controller Profile not enabled for Android");
            }
            var pc = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (pc == null) { SetupLog.Warn("OpenXR settings for Standalone not found yet"); return; }
            ProjectValidator.Check(IsEnabled(pc.GetFeature<OculusTouchControllerProfile>()), "Standalone: Oculus Touch profile enabled (Quest 2 via Virtual Desktop / Link)", "Standalone: Oculus Touch profile not enabled");
            ProjectValidator.Check(IsEnabled(pc.GetFeature<ValveIndexControllerProfile>()) && IsEnabled(pc.GetFeature<HTCViveControllerProfile>()), "Standalone: Valve Index + HTC Vive profiles enabled (SteamVR headsets)", "Standalone: Index/Vive profiles not enabled");
            ProjectValidator.Check(pc.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced, "Standalone: Single Pass Instanced", "Standalone: render mode is not Single Pass Instanced");
        }

        /// <summary>Enabled interaction-profile feature ids for a build target group (tests).</summary>
        public static List<string> EnabledProfileIds(BuildTargetGroup group)
        {
            var result = new List<string>();
            var all = group == BuildTargetGroup.Android ? AndroidProfiles : StandaloneProfiles;
            foreach (var id in all) { var f = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id); if (f != null && f.enabled) result.Add(id); }
            return result;
        }

        static bool IsEnabled(UnityEngine.XR.OpenXR.Features.OpenXRFeature f) => f != null && f.enabled;
    }
}
