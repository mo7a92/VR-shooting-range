using UnityEngine;

namespace VRRange.Core
{
    /// <summary>Logs basic device / XR info on start so logcat shows what ran (useful for the local test session).</summary>
    public class RangeRuntimeInfo : MonoBehaviour
    {
        void Start()
        {
            Application.targetFrameRate = -1; // let the XR runtime drive the refresh rate (72 Hz on Quest 2)
            Debug.Log($"[VRRange] {Application.productName} {Application.version} | Unity {Application.unityVersion} | {SystemInfo.deviceModel} | GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) | XR device: {UnityEngine.XR.XRSettings.loadedDeviceName}");
        }
    }
}
