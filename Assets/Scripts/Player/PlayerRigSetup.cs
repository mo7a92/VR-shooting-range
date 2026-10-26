using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRRange.Player
{
    /// <summary>
    /// Runtime guard for the XR rig: floor-level tracking so the player's real height is used, sensible camera
    /// clip planes, and controller interactors configured for both grabbing and UI rays.
    /// </summary>
    public class PlayerRigSetup : MonoBehaviour
    {
        [SerializeField] XROrigin m_Origin;
        [SerializeField, Range(0.01f, 0.3f)] float m_NearClip = 0.05f;
        [SerializeField, Range(20f, 300f)] float m_FarClip = 120f;

        void Start()
        {
            if (m_Origin == null) m_Origin = GetComponentInChildren<XROrigin>() ?? FindFirstObjectByType<XROrigin>();
            if (m_Origin == null) { Debug.LogWarning("[VRRange] No XROrigin found; the rig may be missing from the scene.", this); return; }
            m_Origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            var cam = m_Origin.Camera;
            if (cam != null) { cam.nearClipPlane = m_NearClip; cam.farClipPlane = m_FarClip; }
            foreach (var nf in m_Origin.GetComponentsInChildren<NearFarInteractor>(true)) nf.enableUIInteraction = true;
            if (!XRSettings.enabled) Debug.LogWarning("[VRRange] XR is not active (XRSettings.enabled == false). On device this means OpenXR did not initialise.");
        }
    }
}
