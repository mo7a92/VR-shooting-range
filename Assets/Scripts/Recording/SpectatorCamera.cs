using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace VRRange.Recording
{
    public enum SpectatorMode { FirstPerson, ThirdPerson, Fixed, Off }

    /// <summary>Pure helpers for the spectator camera (unit-tested without a scene).</summary>
    public static class SpectatorMath
    {
        /// <summary>Normalized viewport rect that letterboxes/pillarboxes a target aspect inside a window.</summary>
        public static Rect LetterboxRect(int screenWidth, int screenHeight, float targetAspect)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || targetAspect <= 0f) return new Rect(0f, 0f, 1f, 1f);
            float windowAspect = (float)screenWidth / screenHeight;
            if (Mathf.Abs(windowAspect - targetAspect) < 1e-3f) return new Rect(0f, 0f, 1f, 1f);
            if (windowAspect > targetAspect)
            {
                float w = targetAspect / windowAspect; // pillarbox
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = windowAspect / targetAspect; // letterbox
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        /// <summary>Exponential smoothing factor that is frame-rate independent.</summary>
        public static float SmoothFactor(float speed, float dt) => speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * Mathf.Max(0f, dt));

        /// <summary>Yaw-only rotation of a head pose (so a third-person camera never rolls or pitches with the player's head).</summary>
        public static Quaternion YawOnly(Quaternion rotation)
        {
            var fwd = rotation * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = rotation * Vector3.up; // looking straight up/down: use the "up" vector projected
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        /// <summary>Third-person camera pose: offset in the head's yaw frame, looking at a point ahead of the head.</summary>
        public static Pose ThirdPersonPose(Vector3 headPosition, Quaternion headRotation, Vector3 offset, float lookAhead)
        {
            var yaw = YawOnly(headRotation);
            var pos = headPosition + yaw * offset;
            var lookAt = headPosition + yaw * Vector3.forward * Mathf.Max(0.5f, lookAhead);
            var dir = lookAt - pos;
            var rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : yaw;
            return new Pose(pos, rot);
        }
    }

    /// <summary>
    /// Desktop spectator / recording camera for PCVR. Renders a clean 16:9 view into the game window (for OBS) that is
    /// independent of the headset view: first-person (smoothed head follow), third-person (yaw-stabilised over-the-shoulder)
    /// or a fixed tripod. It never touches the XR camera or tracking, only reads the head pose. Not used on Android.
    /// Hotkeys (desktop keyboard): F1 first person, F2 third person, F3 fixed, F4 off, F5 cycle third-person presets,
    /// [ / ] field of view, F6 toggle half-rate rendering.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpectatorCamera : MonoBehaviour
    {
        [Header("Wiring (assigned by the scene builder)")]
        [SerializeField] Camera m_Camera;
        [SerializeField] Camera m_BarsCamera;      // clears the whole window to black behind the letterboxed view
        [SerializeField] Transform m_Head;         // the XR camera transform (read-only for us)

        [Header("View")]
        [SerializeField] SpectatorMode m_Mode = SpectatorMode.FirstPerson;
        [SerializeField] float m_TargetAspect = 16f / 9f;
        [SerializeField, Range(30f, 110f)] float m_FieldOfView = 68f;
        [SerializeField] bool m_DisableHeadsetMirror = true;
        [Tooltip("Render the spectator view every Nth frame to reduce GPU cost (1 = every frame).")]
        [SerializeField, Range(1, 4)] int m_RenderEveryNthFrame = 1;
        [SerializeField] bool m_Hotkeys = true;

        [Header("First person")]
        [SerializeField, Range(0f, 40f)] float m_FirstPersonPositionSmoothing = 14f;
        [SerializeField, Range(0f, 40f)] float m_FirstPersonRotationSmoothing = 10f;

        [Header("Third person (head-yaw frame, metres)")]
        [SerializeField] Vector3[] m_ThirdPersonPresets = { new Vector3(0.55f, 0.35f, -1.35f), new Vector3(-0.55f, 0.35f, -1.35f), new Vector3(1.6f, 0.2f, 0.4f), new Vector3(0f, 0.9f, -2.2f) };
        [SerializeField] int m_ThirdPersonPreset;
        [SerializeField] float m_ThirdPersonLookAhead = 4f;
        [SerializeField, Range(0f, 40f)] float m_ThirdPersonSmoothing = 5f;

        [Header("Fixed tripod (world space)")]
        [SerializeField] Vector3 m_FixedPosition = new Vector3(2.4f, 1.75f, -2.4f);
        [SerializeField] Vector3 m_FixedLookAt = new Vector3(0f, 1.3f, 1.5f);

        bool m_Supported, m_HasPose; int m_LastW, m_LastH; float m_LastAspect;
        float m_MessageUntil; string m_Message = "";
        int m_FrameCounter;

        public SpectatorMode Mode => m_Mode;
        public Camera Camera => m_Camera;
        public Transform Head => m_Head;
        public bool IsSupported => m_Supported;
        public int ThirdPersonPreset => m_ThirdPersonPreset;
        public Vector3 ThirdPersonOffset => m_ThirdPersonPresets != null && m_ThirdPersonPresets.Length > 0 ? m_ThirdPersonPresets[Mathf.Clamp(m_ThirdPersonPreset, 0, m_ThirdPersonPresets.Length - 1)] : Vector3.zero;
        public float FieldOfView { get => m_FieldOfView; set { m_FieldOfView = Mathf.Clamp(value, 30f, 110f); if (m_Camera != null) m_Camera.fieldOfView = m_FieldOfView; } }
        public int RenderEveryNthFrame { get => m_RenderEveryNthFrame; set => m_RenderEveryNthFrame = Mathf.Clamp(value, 1, 4); }

        /// <summary>Desktop platforms (and the editor) only: the Quest build never pays for a second camera.</summary>
        public static bool SupportedOnPlatform(RuntimePlatform platform) =>
            platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor ||
            platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor ||
            platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor;

        public void Configure(Camera camera, Camera barsCamera, Transform head)
        {
            m_Camera = camera; m_BarsCamera = barsCamera; m_Head = head;
            ApplyCameraDefaults();
        }

        /// <summary>Camera flags that make this a desktop-only camera that never reaches the headset.</summary>
        public void ApplyCameraDefaults()
        {
            if (m_Camera != null)
            {
                m_Camera.stereoTargetEye = StereoTargetEyeMask.None;
                m_Camera.targetDisplay = 0;
                m_Camera.depth = 50f;
                m_Camera.fieldOfView = m_FieldOfView;
                m_Camera.nearClipPlane = 0.05f; m_Camera.farClipPlane = 120f;
                m_Camera.clearFlags = CameraClearFlags.SolidColor; m_Camera.backgroundColor = Color.black;
                if (m_Camera.CompareTag("MainCamera")) m_Camera.tag = "Untagged";
                var listener = m_Camera.GetComponent<AudioListener>(); if (listener != null) Destroy(listener);
            }
            if (m_BarsCamera != null)
            {
                m_BarsCamera.stereoTargetEye = StereoTargetEyeMask.None;
                m_BarsCamera.targetDisplay = 0;
                m_BarsCamera.depth = 49f;
                m_BarsCamera.cullingMask = 0;
                m_BarsCamera.clearFlags = CameraClearFlags.SolidColor; m_BarsCamera.backgroundColor = Color.black;
                m_BarsCamera.rect = new Rect(0f, 0f, 1f, 1f);
                if (m_BarsCamera.CompareTag("MainCamera")) m_BarsCamera.tag = "Untagged";
            }
        }

        void Awake()
        {
            m_Supported = SupportedOnPlatform(Application.platform);
            if (!m_Supported)
            {
                if (m_Camera != null) m_Camera.gameObject.SetActive(false);
                if (m_BarsCamera != null) m_BarsCamera.gameObject.SetActive(false);
                enabled = false;
                return;
            }
            ApplyCameraDefaults();
        }

        void OnEnable()
        {
            if (!m_Supported) return;
            SetMode(m_Mode, false);
            if (m_DisableHeadsetMirror) SetMirror(false);
        }

        void OnDisable()
        {
            if (m_Camera != null) m_Camera.enabled = false;
            if (m_BarsCamera != null) m_BarsCamera.enabled = false;
            if (m_DisableHeadsetMirror) SetMirror(true);
        }

        static void SetMirror(bool on)
        {
            // The OpenXR mirror blit would otherwise draw the (distorted) headset view into the same window.
            try { XRSettings.gameViewRenderMode = on ? GameViewRenderMode.LeftEye : GameViewRenderMode.None; } catch { }
        }

        public void SetMode(SpectatorMode mode, bool announce = true)
        {
            m_Mode = mode;
            bool on = mode != SpectatorMode.Off && m_Supported;
            if (m_Camera != null) m_Camera.enabled = on;
            if (m_BarsCamera != null) m_BarsCamera.enabled = on;
            m_HasPose = false;
            if (announce) Announce($"Spectator: {mode}{(mode == SpectatorMode.ThirdPerson ? $" (preset {m_ThirdPersonPreset + 1})" : "")}");
            if (on) SnapToTarget();
        }

        public void SetThirdPersonPreset(int index)
        {
            if (m_ThirdPersonPresets == null || m_ThirdPersonPresets.Length == 0) return;
            m_ThirdPersonPreset = ((index % m_ThirdPersonPresets.Length) + m_ThirdPersonPresets.Length) % m_ThirdPersonPresets.Length;
            if (m_Mode == SpectatorMode.ThirdPerson) Announce($"Spectator: third person preset {m_ThirdPersonPreset + 1}");
        }

        public void SetFixedPose(Vector3 position, Vector3 lookAt) { m_FixedPosition = position; m_FixedLookAt = lookAt; m_HasPose = false; }

        /// <summary>The pose the camera is steering towards this frame (tests compare against it).</summary>
        public Pose TargetPose()
        {
            var head = m_Head;
            Vector3 hp = head != null ? head.position : transform.position; Quaternion hr = head != null ? head.rotation : transform.rotation;
            switch (m_Mode)
            {
                case SpectatorMode.ThirdPerson: return SpectatorMath.ThirdPersonPose(hp, hr, ThirdPersonOffset, m_ThirdPersonLookAhead);
                case SpectatorMode.Fixed:
                    var d = m_FixedLookAt - m_FixedPosition;
                    return new Pose(m_FixedPosition, d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity);
                default: return new Pose(hp, hr);
            }
        }

        void SnapToTarget()
        {
            if (m_Camera == null) return;
            var p = TargetPose(); m_Camera.transform.SetPositionAndRotation(p.position, p.rotation); m_HasPose = true;
        }

        void LateUpdate()
        {
            if (!m_Supported || m_Camera == null) return;
            if (m_Head == null) { var main = Camera.main; if (main != null && main != m_Camera) m_Head = main.transform; }
            if (m_Hotkeys) HandleHotkeys();
            if (m_Mode == SpectatorMode.Off) return;
            UpdateViewport();
            // frame skipping: toggle the cameras so the pipeline simply does not schedule them on skipped frames
            m_FrameCounter++;
            bool renderThisFrame = m_RenderEveryNthFrame <= 1 || (m_FrameCounter % m_RenderEveryNthFrame) == 0;
            m_Camera.enabled = renderThisFrame; if (m_BarsCamera != null) m_BarsCamera.enabled = renderThisFrame;

            var target = TargetPose();
            if (!m_HasPose) { m_Camera.transform.SetPositionAndRotation(target.position, target.rotation); m_HasPose = true; return; }
            float dt = Time.unscaledDeltaTime;
            float posK, rotK;
            switch (m_Mode)
            {
                case SpectatorMode.ThirdPerson: posK = rotK = SpectatorMath.SmoothFactor(m_ThirdPersonSmoothing, dt); break;
                case SpectatorMode.Fixed: posK = rotK = 1f; break;
                default: posK = SpectatorMath.SmoothFactor(m_FirstPersonPositionSmoothing, dt); rotK = SpectatorMath.SmoothFactor(m_FirstPersonRotationSmoothing, dt); break;
            }
            var t = m_Camera.transform;
            t.SetPositionAndRotation(Vector3.Lerp(t.position, target.position, posK), Quaternion.Slerp(t.rotation, target.rotation, rotK));
        }

        void UpdateViewport()
        {
            int w = Screen.width, h = Screen.height;
            if (w == m_LastW && h == m_LastH && Mathf.Approximately(m_LastAspect, m_TargetAspect)) return;
            m_LastW = w; m_LastH = h; m_LastAspect = m_TargetAspect;
            m_Camera.rect = SpectatorMath.LetterboxRect(w, h, m_TargetAspect);
            m_Camera.fieldOfView = m_FieldOfView;
        }

        void HandleHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) SetMode(SpectatorMode.FirstPerson);
            else if (kb.f2Key.wasPressedThisFrame) SetMode(SpectatorMode.ThirdPerson);
            else if (kb.f3Key.wasPressedThisFrame) SetMode(SpectatorMode.Fixed);
            else if (kb.f4Key.wasPressedThisFrame) SetMode(SpectatorMode.Off);
            else if (kb.f5Key.wasPressedThisFrame) { SetThirdPersonPreset(m_ThirdPersonPreset + 1); if (m_Mode != SpectatorMode.ThirdPerson) SetMode(SpectatorMode.ThirdPerson); }
            else if (kb.f6Key.wasPressedThisFrame) { RenderEveryNthFrame = m_RenderEveryNthFrame == 1 ? 2 : 1; Announce($"Spectator render rate: every {m_RenderEveryNthFrame} frame(s)"); }
            else if (kb.leftBracketKey.wasPressedThisFrame) { FieldOfView = m_FieldOfView - 5f; Announce($"Spectator FOV {m_FieldOfView:0}"); }
            else if (kb.rightBracketKey.wasPressedThisFrame) { FieldOfView = m_FieldOfView + 5f; Announce($"Spectator FOV {m_FieldOfView:0}"); }
        }

        void Announce(string msg) { m_Message = msg; m_MessageUntil = Time.unscaledTime + 2.5f; Debug.Log("[VRRange] " + msg); }

        void OnGUI()
        {
            if (!m_Supported || Time.unscaledTime > m_MessageUntil || string.IsNullOrEmpty(m_Message)) return;
            GUI.Label(new Rect(16, 12, 600, 28), m_Message);
        }
    }
}
