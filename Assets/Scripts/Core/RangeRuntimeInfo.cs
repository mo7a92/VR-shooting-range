using UnityEngine;

namespace VRRange.Core
{
    /// <summary>Logs device / XR / OpenXR-runtime info on start so logs show exactly what ran (Quest, Virtual Desktop VDXR, SteamVR...).</summary>
    public class RangeRuntimeInfo : MonoBehaviour
    {
        void Start()
        {
            Application.targetFrameRate = -1; // the XR runtime drives the refresh rate (72 Hz on Quest 2, 90+ Hz on PCVR)
            Debug.Log($"[VRRange] {Application.productName} {Application.version} | Unity {Application.unityVersion} | {Application.platform} | {SystemInfo.deviceModel} | GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) | XR device: {UnityEngine.XR.XRSettings.loadedDeviceName} | {OpenXRRuntimeDescription()}");
        }

        /// <summary>"OpenXR runtime: name version" when the OpenXR plug-in is loaded, read via reflection so this assembly does not depend on it.</summary>
        public static string OpenXRRuntimeDescription()
        {
            try
            {
                var t = System.Type.GetType("UnityEngine.XR.OpenXR.OpenXRRuntime, Unity.XR.OpenXR");
                if (t == null) return "OpenXR plug-in not present";
                string name = t.GetProperty("name")?.GetValue(null) as string;
                string version = t.GetProperty("version")?.GetValue(null) as string;
                string api = t.GetProperty("apiVersion")?.GetValue(null) as string;
                return string.IsNullOrEmpty(name) ? "OpenXR runtime: not initialised" : $"OpenXR runtime: {name} {version} (API {api})";
            }
            catch (System.Exception e) { return $"OpenXR runtime: unknown ({e.GetType().Name})"; }
        }
    }
}
