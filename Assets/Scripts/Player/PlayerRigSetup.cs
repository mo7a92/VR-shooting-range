using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace VRRange.Player
{
    /// <summary>
    /// Runtime guard for the XR rig: floor-level tracking so the player's real height is used, sensible camera
    /// clip planes, and controller interactors configured for both grabbing and UI rays.
    /// Also provides a floor calibration, because some PC runtimes report a wrong floor for the STAGE space
    /// (OpenXR 1.16 maps Floor mode to STAGE): hold a controller on the real floor and click both thumbsticks
    /// for one second, or use PageUp / PageDown (+/- 2 cm) and Home (reset) on the keyboard. Saved in PlayerPrefs.
    /// </summary>
    public class PlayerRigSetup : MonoBehaviour
    {
        const string k_PrefKey = "VRRange.FloorOffset";
        const float k_ControllerRestHeight = 0.03f; // controller centre above the floor when resting on it
        const float k_HoldSeconds = 1f;
        const float k_KeyStep = 0.02f;
        const float k_MaxOffset = 1.5f;

        [SerializeField] XROrigin m_Origin;
        [SerializeField, Range(0.01f, 0.3f)] float m_NearClip = 0.05f;
        [SerializeField, Range(20f, 300f)] float m_FarClip = 120f;

        float m_FloorOffset;
        float m_HoldTimer;
        bool m_HoldConsumed;
        bool m_HeightLogged;
        float m_StartTime;
        InputAction m_StickL, m_StickR, m_PosL, m_PosR;

        /// <summary>Extra height added to the tracked floor (metres). Positive raises the player.</summary>
        public float FloorOffset => m_FloorOffset;

        void Start()
        {
            if (m_Origin == null) m_Origin = GetComponentInChildren<XROrigin>() ?? FindFirstObjectByType<XROrigin>();
            if (m_Origin == null) { Debug.LogWarning("[VRRange] No XROrigin found; the rig may be missing from the scene.", this); return; }
            m_Origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            var cam = m_Origin.Camera;
            if (cam != null) { cam.nearClipPlane = m_NearClip; cam.farClipPlane = m_FarClip; }
            int weaponLayer = LayerMask.NameToLayer("Weapon");
            int extraMask = weaponLayer >= 0 ? 1 << weaponLayer : 0;
            foreach (var nf in m_Origin.GetComponentsInChildren<NearFarInteractor>(true))
            {
                nf.enableUIInteraction = true;
                // The XRI Starter Assets casters only query the Default layer (near) and Default/UI (far); weapons live on
                // the "Weapon" physics layer, so without this neither touch-grab nor distance-grab can ever find them.
                if (extraMask == 0) continue;
                if (nf.nearInteractionCaster is SphereInteractionCaster near) near.physicsLayerMask |= extraMask;
                if (nf.farInteractionCaster is CurveInteractionCaster far) far.raycastMask |= extraMask;
                Debug.Log($"[VRRange] {nf.name}: grab casters now include the Weapon layer (near {(nf.nearInteractionCaster as SphereInteractionCaster)?.physicsLayerMask.value}, far {(nf.farInteractionCaster as CurveInteractionCaster)?.raycastMask.value})");
            }
            if (!XRSettings.enabled) Debug.LogWarning("[VRRange] XR is not active (XRSettings.enabled == false). On device this means OpenXR did not initialise.");

            m_FloorOffset = Mathf.Clamp(SafeGetPref(), -k_MaxOffset, k_MaxOffset);
            m_StickL = MakeAction("VRRange/StickClickL", "<XRController>{LeftHand}/{Primary2DAxisClick}", InputActionType.Button);
            m_StickR = MakeAction("VRRange/StickClickR", "<XRController>{RightHand}/{Primary2DAxisClick}", InputActionType.Button);
            m_PosL = MakeAction("VRRange/PosL", "<XRController>{LeftHand}/devicePosition", InputActionType.Value);
            m_PosR = MakeAction("VRRange/PosR", "<XRController>{RightHand}/devicePosition", InputActionType.Value);
            m_StartTime = Time.unscaledTime;
            if (m_FloorOffset != 0f) Debug.Log($"[VRRange] Floor offset loaded: {m_FloorOffset:+0.00;-0.00} m");
        }

        static InputAction MakeAction(string name, string binding, InputActionType type)
        {
            var a = new InputAction(name, type, binding);
            a.Enable();
            return a;
        }

        void OnDestroy()
        {
            m_StickL?.Dispose(); m_StickR?.Dispose(); m_PosL?.Dispose(); m_PosR?.Dispose();
        }

        void LateUpdate()
        {
            if (m_Origin == null || m_StickL == null) return;
            HandleKeyboard();
            HandleControllerCalibration();
            ApplyOffset();
            LogHeightOnce();
        }

        void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.pageUpKey.wasPressedThisFrame) SetOffset(m_FloorOffset + k_KeyStep, "PageUp");
            else if (kb.pageDownKey.wasPressedThisFrame) SetOffset(m_FloorOffset - k_KeyStep, "PageDown");
            else if (kb.homeKey.wasPressedThisFrame) SetOffset(0f, "Home (reset)");
        }

        void HandleControllerCalibration()
        {
            bool both = m_StickL.IsPressed() && m_StickR.IsPressed();
            if (!both) { m_HoldTimer = 0f; m_HoldConsumed = false; return; }
            if (m_HoldConsumed) return;
            m_HoldTimer += Time.unscaledDeltaTime;
            if (m_HoldTimer < k_HoldSeconds) return;
            m_HoldConsumed = true;

            // devicePosition is in tracking space (child of the camera floor offset), so the floor in tracking
            // space is at -offset. Put the lowest controller k_ControllerRestHeight above the game floor.
            float lowest = Mathf.Min(m_PosL.ReadValue<Vector3>().y, m_PosR.ReadValue<Vector3>().y);
            SetOffset(k_ControllerRestHeight - lowest, "controller on floor");
        }

        void SetOffset(float value, string source)
        {
            m_FloorOffset = Mathf.Clamp(value, -k_MaxOffset, k_MaxOffset);
            try { PlayerPrefs.SetFloat(k_PrefKey, m_FloorOffset); PlayerPrefs.Save(); } catch { /* prefs unavailable */ }
            var cam = m_Origin.Camera;
            float eye = cam != null ? cam.transform.localPosition.y + m_FloorOffset : float.NaN;
            Debug.Log($"[VRRange] Floor calibrated ({source}): offset {m_FloorOffset:+0.00;-0.00} m, eye height now {eye:0.00} m");
        }

        void ApplyOffset()
        {
            // Only override in Floor mode; in Device mode XROrigin owns the camera Y offset.
            if (m_Origin.CurrentTrackingOriginMode != TrackingOriginModeFlags.Floor) return;
            var offsetObj = m_Origin.CameraFloorOffsetObject;
            if (offsetObj == null) return;
            var t = offsetObj.transform;
            var p = t.localPosition;
            if (!Mathf.Approximately(p.y, m_FloorOffset)) { p.y = m_FloorOffset; t.localPosition = p; }
        }

        void LogHeightOnce()
        {
            if (m_HeightLogged || !XRSettings.enabled || Time.unscaledTime - m_StartTime < 5f) return;
            m_HeightLogged = true;
            var cam = m_Origin.Camera;
            if (cam == null) return;
            float tracked = cam.transform.localPosition.y;
            Debug.Log($"[VRRange] Tracking origin {m_Origin.CurrentTrackingOriginMode}; tracked eye height {tracked:0.00} m, floor offset {m_FloorOffset:+0.00;-0.00} m, in-game eye height {tracked + m_FloorOffset:0.00} m");
            if (tracked + m_FloorOffset < 1.1f)
                Debug.LogWarning("[VRRange] Eye height looks low. Put a controller on the floor and click both thumbsticks for 1 s, or press PageUp.");
        }

        static float SafeGetPref()
        {
            try { return PlayerPrefs.GetFloat(k_PrefKey, 0f); } catch { return 0f; }
        }
    }
}
