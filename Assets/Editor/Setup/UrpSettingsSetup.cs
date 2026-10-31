// NOTE: this file is excluded from the cloud compile check (URP is not part of the reference build). The API used
// here was verified against com.unity.render-pipelines.universal@17.3.0 source by inspection.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VRRange.Editor
{
    /// <summary>
    /// Two URP presets: a mobile-VR tuned asset for Quest 2 (default pipeline, quality level 0) and a high-end PCVR asset
    /// (quality level 1: HDR, 4 shadow cascades at 4K, soft shadows, per-pixel additional lights with shadows, SSAO,
    /// post-processing profile). Idempotent: existing assets are updated in place.
    /// </summary>
    public static class UrpSettingsSetup
    {
        public static UniversalRenderPipelineAsset Apply()
        {
            SetupLog.Section("URP");
            RangePaths.EnsureFolder(RangePaths.Settings);
            var quest = BuildQuestPreset();
            var pcvr = BuildPcvrPreset();
            BuildPcvrPostProfile();

            GraphicsSettings.defaultRenderPipeline = quest;
            int current = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = names[i] == ProjectSettingsSetup.PcvrQualityName ? pcvr : quest;
            }
            QualitySettings.SetQualityLevel(Mathf.Clamp(current, 0, names.Length - 1), false);
            AssetDatabase.SaveAssets();
            SetupLog.Ok($"URP presets assigned: {quest.name} -> quality '{ProjectSettingsSetup.QuestQualityName}' (+ default pipeline), {pcvr.name} -> quality '{ProjectSettingsSetup.PcvrQualityName}'");
            return quest;
        }

        static UniversalRendererData Renderer(string path, bool depthPriming)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, path); }
            renderer.renderingMode = RenderingMode.Forward;
            renderer.depthPrimingMode = depthPriming ? DepthPrimingMode.Auto : DepthPrimingMode.Disabled;
            renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
            EditorUtility.SetDirty(renderer);
            return renderer;
        }

        static UniversalRenderPipelineAsset Asset(string path, UniversalRendererData renderer)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null) { asset = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(asset, path); }
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            if (list != null) { list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = renderer; }
            var def = so.FindProperty("m_DefaultRendererIndex"); if (def != null) def.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        public static UniversalRenderPipelineAsset BuildQuestPreset()
        {
            var renderer = Renderer(RangePaths.UrpRenderer, false);
            RemoveFeature<ScreenSpaceAmbientOcclusion>(renderer);
            var asset = Asset(RangePaths.UrpAsset, renderer);
            asset.msaaSampleCount = 4;
            asset.supportsHDR = false;
            asset.renderScale = 1.0f;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.mainLightShadowmapResolution = 1024;
            asset.shadowDistance = 18f;
            asset.shadowCascadeCount = 1;
            asset.maxAdditionalLightsCount = 4;
            asset.useSRPBatcher = true;
            asset.colorGradingMode = ColorGradingMode.LowDynamicRange;
            asset.upscalingFilter = UpscalingFilterSelection.Auto;
            var so = new SerializedObject(asset);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_SoftShadowsSupported", false);
            SetEnum(so, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.PerVertex);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            SetupLog.Ok("URP Quest 2 preset: Forward, 4x MSAA, no HDR, 1 cascade @1024, hard shadows, per-vertex additional lights, no depth/opaque textures");
            return asset;
        }

        public static UniversalRenderPipelineAsset BuildPcvrPreset()
        {
            var renderer = Renderer(RangePaths.UrpPcvrRenderer, false);
            EnsureSsao(renderer);
            var asset = Asset(RangePaths.UrpPcvrAsset, renderer);
            asset.msaaSampleCount = 4;
            asset.supportsHDR = true;
            asset.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
            asset.renderScale = 1.0f; // supersampling is better done by the runtime (Virtual Desktop / SteamVR per-app resolution)
            asset.supportsCameraDepthTexture = true;
            asset.supportsCameraOpaqueTexture = false;
            asset.mainLightShadowmapResolution = 4096;
            asset.additionalLightsShadowmapResolution = 2048;
            asset.shadowDistance = 45f;
            asset.shadowCascadeCount = 4;
            asset.cascade4Split = new Vector3(0.08f, 0.25f, 0.55f);
            asset.shadowDepthBias = 1f; asset.shadowNormalBias = 1f;
            asset.maxAdditionalLightsCount = 8;
            asset.useSRPBatcher = true;
            asset.colorGradingMode = ColorGradingMode.HighDynamicRange;
            asset.colorGradingLutSize = 32;
            asset.upscalingFilter = UpscalingFilterSelection.Auto;
            var so = new SerializedObject(asset);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetBool(so, "m_AdditionalLightShadowsSupported", true);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetBool(so, "m_ReflectionProbeBlending", true);
            SetBool(so, "m_ReflectionProbeBoxProjection", true);
            SetEnum(so, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.PerPixel);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            SetupLog.Ok("URP PCVR preset: Forward, 4x MSAA, HDR, 4 cascades @4096 soft shadows, per-pixel additional lights with shadows, SSAO, probe blending");
            return asset;
        }

        /// <summary>Adds the URP Screen Space Ambient Occlusion renderer feature once (sub-asset of the renderer).</summary>
        static void EnsureSsao(UniversalRendererData renderer)
        {
            ScreenSpaceAmbientOcclusion ssao = null;
            foreach (var f in renderer.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion s) { ssao = s; break; }
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                var so = new SerializedObject(renderer);
                var list = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = ssao;
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId)) { map.arraySize = list.arraySize; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId; }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            // settings are internal fields: write them through the serialized object (names verified in URP 17.3 source)
            var sso = new SerializedObject(ssao);
            var st = sso.FindProperty("m_Settings");
            if (st != null)
            {
                void F(string n, float v) { var p = st.FindPropertyRelative(n); if (p != null) p.floatValue = v; }
                void B(string n, bool v) { var p = st.FindPropertyRelative(n); if (p != null) p.boolValue = v; }
                void E(string n, int v) { var p = st.FindPropertyRelative(n); if (p != null) p.enumValueIndex = v; }
                F("Intensity", 1.6f); F("Radius", 0.3f); F("Falloff", 60f); F("DirectLightingStrength", 0.35f);
                B("Downsample", false); B("AfterOpaque", false);
                E("Source", 0 /*DepthNormals*/); E("NormalSamples", 1 /*Medium*/); E("Samples", 1 /*Medium*/); E("AOMethod", 0 /*BlueNoise*/);
                sso.ApplyModifiedPropertiesWithoutUndo();
            }
            ssao.SetActive(true);
            EditorUtility.SetDirty(ssao); EditorUtility.SetDirty(renderer);
        }

        static void RemoveFeature<T>(UniversalRendererData renderer) where T : ScriptableRendererFeature
        {
            bool removed = false;
            for (int i = renderer.rendererFeatures.Count - 1; i >= 0; i--)
            {
                if (!(renderer.rendererFeatures[i] is T f)) continue;
                renderer.rendererFeatures.RemoveAt(i);
                var so = new SerializedObject(renderer); var map = so.FindProperty("m_RendererFeatureMap");
                if (map != null && i < map.arraySize) map.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.RemoveObjectFromAsset(f); Object.DestroyImmediate(f, true); removed = true;
            }
            if (removed) EditorUtility.SetDirty(renderer);
        }

        /// <summary>Subtle, VR-safe post-processing for PCVR: ACES tonemapping, gentle bloom on emissives, light colour grading.</summary>
        public static VolumeProfile BuildPcvrPostProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RangePaths.PcvrPostProfile);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, RangePaths.PcvrPostProfile); }
            T Comp<T>() where T : VolumeComponent
            {
                if (profile.TryGet<T>(out var existing)) return existing;
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            var tone = Comp<Tonemapping>(); tone.active = true; tone.mode.overrideState = true; tone.mode.value = TonemappingMode.ACES;
            var bloom = Comp<Bloom>(); bloom.active = true;
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.1f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.22f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.6f;
            bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = true;
            var color = Comp<ColorAdjustments>(); color.active = true;
            color.postExposure.overrideState = true; color.postExposure.value = 0.15f;
            color.contrast.overrideState = true; color.contrast.value = 6f;
            color.saturation.overrideState = true; color.saturation.value = 4f;
            EditorUtility.SetDirty(profile);
            foreach (var c in profile.components) EditorUtility.SetDirty(c);
            SetupLog.Ok("PCVR post-processing profile: ACES tonemapping, soft bloom, light colour grading (no vignette / motion blur / DoF in VR)");
            return profile;
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value; else SetupLog.Warn($"URP asset field '{name}' not found; set it manually in {so.targetObject.name}");
        }

        static void SetEnum(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.enumValueIndex = value; else SetupLog.Warn($"URP asset field '{name}' not found; set it manually in {so.targetObject.name}");
        }
    }
}
