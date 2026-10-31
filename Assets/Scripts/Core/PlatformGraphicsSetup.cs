using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VRRange.Core
{
    /// <summary>
    /// Applies the per-platform graphics profile at runtime: the PCVR preset (post-processing volume, extra shadow-casting
    /// lights, post-processing on the XR and spectator cameras) is enabled only on desktop platforms; the Quest 2 build
    /// keeps everything off. The URP asset itself is selected by the per-platform quality level (Quest 2 / PCVR).
    /// </summary>
    public class PlatformGraphicsSetup : MonoBehaviour
    {
        [SerializeField] Volume m_PostProcessVolume;
        [SerializeField] Light[] m_PcvrShadowLights;
        [SerializeField] Camera[] m_PostProcessCameras;
        [SerializeField] bool m_ForcePcvrInEditor;

        public bool AppliedPcvr { get; private set; }

        public static bool IsPcvrPlatform(RuntimePlatform platform) =>
            platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor ||
            platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor ||
            platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor;

        public void Configure(Volume volume, Light[] shadowLights, Camera[] postCameras) { m_PostProcessVolume = volume; m_PcvrShadowLights = shadowLights; m_PostProcessCameras = postCameras; }

        void Awake()
        {
            Apply(IsPcvrPlatform(Application.platform) || (Application.isEditor && m_ForcePcvrInEditor));
        }

        /// <summary>Public so tests can flip between the two profiles without rebuilding.</summary>
        public void Apply(bool pcvr)
        {
            AppliedPcvr = pcvr;
            if (pcvr) Application.runInBackground = true; // the headset keeps rendering while the desktop window is unfocused (OBS, Virtual Desktop)
            if (m_PostProcessVolume != null) m_PostProcessVolume.enabled = pcvr;
            if (m_PcvrShadowLights != null)
                foreach (var l in m_PcvrShadowLights) if (l != null) l.shadows = pcvr ? LightShadows.Soft : LightShadows.None;
            if (m_PostProcessCameras != null)
                foreach (var c in m_PostProcessCameras)
                {
                    if (c == null) continue;
                    var data = c.GetUniversalAdditionalCameraData();
                    if (data == null) continue;
                    data.renderPostProcessing = pcvr;
                    data.antialiasing = AntialiasingMode.None; // MSAA from the URP asset; no per-camera FXAA/SMAA in VR
                    data.renderShadows = true;
                }
            Debug.Log($"[VRRange] Graphics profile: {(pcvr ? "PCVR" : "Quest 2")} | quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}' | URP asset {(QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : (GraphicsSettings.defaultRenderPipeline != null ? GraphicsSettings.defaultRenderPipeline.name : "none"))}");
        }
    }
}
