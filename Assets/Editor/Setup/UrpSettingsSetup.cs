// NOTE: this file is excluded from the cloud compile check (URP is not part of the reference build). The API used
// here was verified against com.unity.render-pipelines.universal@17.3.0 source by inspection.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VRRange.Editor
{
    /// <summary>Creates a mobile-VR tuned URP asset + renderer and makes it the active pipeline for all quality levels.</summary>
    public static class UrpSettingsSetup
    {
        public static UniversalRenderPipelineAsset Apply()
        {
            SetupLog.Section("URP");
            RangePaths.EnsureFolder(RangePaths.Settings);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RangePaths.UrpRenderer);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RangePaths.UrpRenderer);
            }
            renderer.renderingMode = RenderingMode.Forward;
            renderer.depthPrimingMode = DepthPrimingMode.Disabled;
            renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RangePaths.UrpAsset);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, RangePaths.UrpAsset);
            }
            asset.msaaSampleCount = 4;
            asset.supportsHDR = false;
            asset.renderScale = 1.0f;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.supportsMainLightShadows = true;
            asset.mainLightShadowmapResolution = 1024;
            asset.shadowDistance = 18f;
            asset.shadowCascadeCount = 1;
            asset.additionalLightsRenderingMode = LightRenderingMode.PerVertex;
            asset.maxAdditionalLightsCount = 4;
            asset.supportsAdditionalLightShadows = false;
            asset.useSRPBatcher = true;
            asset.colorGradingMode = ColorGradingMode.LowDynamicRange;
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(Mathf.Clamp(QualitySettings.names.Length - 1, 0, 5), false);
            AssetDatabase.SaveAssets();
            SetupLog.Ok("URP asset assigned (Forward, 4x MSAA, no HDR, no depth/opaque textures, per-vertex additional lights)");
            return asset;
        }
    }
}
