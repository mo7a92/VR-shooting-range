// NOTE: this file is excluded from the cloud compile check (it needs Unity.XR.OpenXR.Editor, which is not part of
// the reference build). Every API below was verified against com.unity.xr.openxr@1.16.1 source by inspection.
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace VRRange.Editor
{
    /// <summary>Enables the Meta Quest feature group and the Oculus Touch controller profile for Android OpenXR.</summary>
    public static class OpenXRSettingsSetup
    {
        public static void Apply()
        {
            SetupLog.Section("OpenXR");
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                FeatureHelpers.RefreshFeatures(group);
                var touch = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, OculusTouchControllerProfile.featureId);
                if (touch != null) { if (!touch.enabled) { touch.enabled = true; EditorUtility.SetDirty(touch); } SetupLog.Ok($"{group}: Oculus Touch Controller Profile enabled"); }
                else SetupLog.Warn($"{group}: Oculus Touch Controller Profile feature not found (OpenXR package missing or not yet imported)");

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
                    settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
                    EditorUtility.SetDirty(settings);
                    SetupLog.Ok($"{group}: render mode Single Pass Instanced");
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Validation-only: reports whether the Quest feature and Touch profile are enabled for Android.</summary>
        public static void ValidateFeatures()
        {
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (oxr == null) { SetupLog.Warn("OpenXR settings for Android not found yet (they are created when the OpenXR loader is enabled)"); return; }
            var quest = oxr.GetFeature<MetaQuestFeature>();
            ProjectValidator.Check(quest != null && quest.enabled, "Meta Quest Support feature enabled", "Meta Quest Support feature not enabled (Project Settings > XR Plug-in Management > OpenXR > Android)");
            var touch = oxr.GetFeature<OculusTouchControllerProfile>();
            ProjectValidator.Check(touch != null && touch.enabled, "Oculus Touch Controller Profile enabled", "Oculus Touch Controller Profile not enabled");
        }
    }
}
