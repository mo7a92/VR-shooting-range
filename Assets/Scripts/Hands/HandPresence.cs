using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Weapons;

namespace VRRange.Hands
{
    /// <summary>
    /// Visible, animated VR hand driven by controller input and weapon grip anchors.
    /// - Follows the controller with a configurable palm offset when empty; snaps smoothly onto the weapon's grip anchor
    ///   while holding, so hands never float away from or penetrate the grip.
    /// - Finger curl is blended between poses; grip/trigger values animate the free hand; the trigger finger follows the
    ///   trigger on weapon poses. Curl axes are auto-calibrated from the rig's rest pose (works for the XR Hands rig).
    /// - Hides the controller model while a hand is shown.
    /// </summary>
    public class HandPresence : MonoBehaviour
    {
        public enum Finger { Thumb, Index, Middle, Ring, Little }

        [Header("Setup")]
        [SerializeField] bool m_IsLeft;
        [SerializeField] Transform m_HandRoot;              // instantiated hand model root (contains the wrist joint hierarchy)
        [SerializeField] Transform m_ControllerAttach;      // the controller's attach/grip transform to follow (defaults to parent)
        [SerializeField] HandPose m_OpenPose;
        [SerializeField] HandPose m_FistPose;
        [SerializeField] HandPose m_IdlePose;
        [SerializeField] HandPose m_PinchPose;
        [SerializeField] bool m_HideControllerModel = true;

        [Header("Free-hand palm pose relative to the controller attach (holding the Touch controller)")]
        [SerializeField] Vector3 m_FreePalmOffset = new Vector3(0f, -0.01f, -0.02f);
        [SerializeField] Vector3 m_FreePalmEuler = new Vector3(-40f, 0f, 0f);

        [Header("Tuning")]
        [SerializeField, Range(2f, 40f)] float m_FollowSpeed = 25f;
        [SerializeField, Range(2f, 40f)] float m_CurlSpeed = 14f;
        [SerializeField] float m_ProximalMaxDegrees = 80f;
        [SerializeField] float m_IntermediateMaxDegrees = 95f;
        [SerializeField] float m_DistalMaxDegrees = 60f;
        [SerializeField] float m_ThumbSpreadMaxDegrees = 35f;

        // joint data
        class JointChain { public Transform metacarpal, proximal, intermediate, distal, tip; public Quaternion[] rest = new Quaternion[4]; public Vector3[] curlAxisLocal = new Vector3[4]; public Vector3 spreadAxisLocal; }
        readonly JointChain[] m_Fingers = new JointChain[5];
        Transform m_Wrist, m_Palm;
        Vector3 m_PalmLocalPos; Quaternion m_PalmLocalRot = Quaternion.identity; // palm pose in hand-root space
        bool m_Calibrated;

        // state
        readonly float[] m_Curl = new float[5];
        float m_Spread = 0.4f;
        readonly float[] m_TargetCurl = new float[5];
        float m_TargetSpread;
        Weapon m_HeldWeapon; WeaponGripAnchor m_Anchor; HandPose m_OverridePose; float m_OverrideUntil;
        readonly List<XRBaseInputInteractor> m_Interactors = new List<XRBaseInputInteractor>();
        Renderer[] m_ControllerRenderers;

        public bool IsLeft => m_IsLeft;
        public Weapon HeldWeapon => m_HeldWeapon;
        public WeaponGripAnchor CurrentAnchor => m_Anchor;
        public bool IsCalibrated => m_Calibrated;
        public float Curl(Finger f) => m_Curl[(int)f];
        public Transform HandRoot => m_HandRoot;

        public void Configure(bool isLeft, Transform handRoot, Transform controllerAttach, HandPose open, HandPose fist, HandPose idle, HandPose pinch)
        {
            m_IsLeft = isLeft; m_HandRoot = handRoot; m_ControllerAttach = controllerAttach; m_OpenPose = open; m_FistPose = fist; m_IdlePose = idle; m_PinchPose = pinch;
        }

        void Awake()
        {
            if (m_ControllerAttach == null) m_ControllerAttach = transform.parent != null ? transform.parent : transform;
            if (m_HandRoot == null) m_HandRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;
            Calibrate();
        }

        void OnEnable()
        {
            // find the controller's interactors (siblings / parent) to learn what this hand is holding
            // scope: the controller object this hand belongs to (its interactors and its controller model)
            var scope = m_ControllerAttach != null ? m_ControllerAttach : (transform.parent != null ? transform.parent : transform);
            m_Interactors.Clear();
            scope.GetComponentsInChildren(true, m_Interactors);
            foreach (var i in m_Interactors) { i.selectEntered.AddListener(OnSelectEntered); i.selectExited.AddListener(OnSelectExited); }
            if (m_HideControllerModel) SetControllerModelVisible(false);
        }

        void OnDisable()
        {
            foreach (var i in m_Interactors) { if (i == null) continue; i.selectEntered.RemoveListener(OnSelectEntered); i.selectExited.RemoveListener(OnSelectExited); }
            if (m_HideControllerModel) SetControllerModelVisible(true);
        }

        void SetControllerModelVisible(bool visible)
        {
            if (m_ControllerRenderers == null)
            {
                var list = new List<Renderer>();
                var scope = m_ControllerAttach != null ? m_ControllerAttach : transform.parent;
                if (scope != null)
                    foreach (var r in scope.GetComponentsInChildren<Renderer>(true))
                        if (!r.transform.IsChildOf(transform) && !(r is LineRenderer) && !(r is TrailRenderer) && !(r is ParticleSystemRenderer)) list.Add(r);
                m_ControllerRenderers = list.ToArray();
            }
            foreach (var r in m_ControllerRenderers) if (r != null) r.enabled = visible;
        }

        // ---------- calibration ----------
        /// <summary>Finds the XR Hands rig joints and derives per-joint curl axes from the rest pose (no hard-coded axis conventions).</summary>
        public void Calibrate()
        {
            m_Calibrated = false;
            if (m_HandRoot == null) return;
            string p = m_IsLeft ? "L_" : "R_";
            var all = m_HandRoot.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) { foreach (var t in all) if (t.name == p + n || t.name == n) return t; return null; }
            m_Wrist = Find("Wrist"); m_Palm = Find("Palm");
            if (m_Wrist == null) { Debug.LogWarning($"[VRRange] HandPresence ({(m_IsLeft ? "L" : "R")}): no wrist joint found under {m_HandRoot.name}; using static hand.", this); return; }
            string[] names = { "Thumb", "Index", "Middle", "Ring", "Little" };
            var indexProx = Find("IndexProximal"); var littleProx = Find("LittleProximal"); var middleProx = Find("MiddleProximal");
            if (indexProx == null || littleProx == null || middleProx == null) { Debug.LogWarning("[VRRange] HandPresence: finger joints not found", this); return; }
            // Palm inward normal: the finger-plane normal, oriented toward the side the (opposed) thumb tip sits on in the
            // rest pose. Falls back to the handedness rule (palm-down bind pose) if the thumb lies in the plane.
            var cross = Vector3.Cross(indexProx.position - m_Wrist.position, littleProx.position - m_Wrist.position).normalized;
            var thumbTip = Find("ThumbTip"); var thumbMeta = Find("ThumbMetacarpal");
            float thumbSide = thumbTip != null ? Vector3.Dot(cross, thumbTip.position - (thumbMeta != null ? thumbMeta.position : m_Wrist.position)) : 0f;
            float handSize = Vector3.Distance(m_Wrist.position, middleProx.position);
            Vector3 palmInward;
            if (Mathf.Abs(thumbSide) > handSize * 0.08f) palmInward = thumbSide > 0f ? cross : -cross;
            else palmInward = m_IsLeft ? cross : -cross;
            var fingerDir = (middleProx.position - m_Wrist.position).normalized;
            // Palm pose in hand-root space: centre between wrist and knuckles, forward along fingers, up = back of hand.
            var palmCentre = Vector3.Lerp(m_Wrist.position, (indexProx.position + littleProx.position) * 0.5f, 0.6f);
            var palmRot = Quaternion.LookRotation(fingerDir, -palmInward);
            m_PalmLocalPos = m_HandRoot.InverseTransformPoint(palmCentre);
            m_PalmLocalRot = Quaternion.Inverse(m_HandRoot.rotation) * palmRot;
            for (int f = 0; f < 5; f++)
            {
                var c = new JointChain
                {
                    metacarpal = Find(names[f] + "Metacarpal"), proximal = Find(names[f] + "Proximal"),
                    intermediate = Find(names[f] + "Intermediate"), distal = Find(names[f] + "Distal"), tip = Find(names[f] + "Tip")
                };
                if (c.proximal == null || c.tip == null) { m_Fingers[f] = null; continue; }
                Transform[] joints = { c.metacarpal, c.proximal, c.intermediate, c.distal };
                for (int j = 0; j < 4; j++)
                {
                    var t = joints[j]; if (t == null) continue;
                    c.rest[j] = t.localRotation;
                    var next = j < 3 && joints[j + 1] != null ? joints[j + 1] : c.tip;
                    var dir = (next.position - t.position).normalized;
                    var inward = f == 0 ? Vector3.Cross(Vector3.Cross(dir, palmInward), dir).normalized : palmInward; // thumb bends in its own plane toward the palm
                    var axisWorld = Vector3.Cross(dir, inward).normalized;
                    if (axisWorld.sqrMagnitude < 1e-6f) axisWorld = Vector3.Cross(dir, fingerDir).normalized;
                    c.curlAxisLocal[j] = t.InverseTransformDirection(axisWorld);
                }
                if (f == 0 && c.metacarpal != null)
                    c.spreadAxisLocal = c.metacarpal.InverseTransformDirection(-palmInward); // spread = rotate about the back-of-hand normal
                m_Fingers[f] = c;
            }
            m_Calibrated = true;
            // Empirical sign check (rotation handedness differs between conventions): bend the index proximal by +10 deg
            // and verify the tip moved toward the palm; if not, flip every curl axis. Rest pose is restored afterwards.
            var idx = m_Fingers[1];
            if (idx != null && idx.proximal != null && idx.tip != null)
            {
                float before = Vector3.Dot(idx.tip.position - palmCentre, palmInward);
                var saved = idx.proximal.localRotation;
                idx.proximal.localRotation = idx.rest[1] * Quaternion.AngleAxis(10f, idx.curlAxisLocal[1]);
                float after = Vector3.Dot(idx.tip.position - palmCentre, palmInward);
                idx.proximal.localRotation = saved;
                if (after < before)
                {
                    foreach (var c in m_Fingers) { if (c == null) continue; for (int j = 0; j < 4; j++) c.curlAxisLocal[j] = -c.curlAxisLocal[j]; c.spreadAxisLocal = -c.spreadAxisLocal; }
                }
                Debug.Log($"[VRRange] Hand {(m_IsLeft ? "L" : "R")} calibrated: size {handSize:0.000} m, thumbSide {thumbSide:0.0000}, curl sign {(after < before ? "flipped" : "kept")}");
            }
        }

        // ---------- interaction events ----------
        void OnSelectEntered(SelectEnterEventArgs args)
        {
            var weapon = args.interactableObject?.transform != null ? args.interactableObject.transform.GetComponent<Weapon>() : null;
            if (weapon == null) return;
            m_HeldWeapon = weapon;
            m_Anchor = weapon.GetGripAnchorFor(args.interactorObject);
            weapon.GripChanged += OnWeaponGripChanged;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            var weapon = args.interactableObject?.transform != null ? args.interactableObject.transform.GetComponent<Weapon>() : null;
            if (weapon == null || weapon != m_HeldWeapon) return;
            weapon.GripChanged -= OnWeaponGripChanged;
            m_HeldWeapon = null; m_Anchor = null;
        }

        void OnWeaponGripChanged(Weapon w)
        {
            if (w != m_HeldWeapon) return;
            foreach (var i in m_Interactors) if (i != null && i.hasSelection) { m_Anchor = w.GetGripAnchorFor(i); if (m_Anchor != null) break; }
        }

        /// <summary>Temporarily force a pose (e.g. pinch while reloading).</summary>
        public void OverridePose(HandPose pose, float seconds) { m_OverridePose = pose; m_OverrideUntil = Time.time + seconds; }

        // ---------- per-frame ----------
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float grip = HandInput.Grip(m_IsLeft), trigger = HandInput.Trigger(m_IsLeft);
            UpdateTargetPose();
            ComputeTargetCurls(grip, trigger);
            float k = 1f - Mathf.Exp(-m_CurlSpeed * dt);
            for (int i = 0; i < 5; i++) m_Curl[i] = Mathf.Lerp(m_Curl[i], m_TargetCurl[i], k);
            m_Spread = Mathf.Lerp(m_Spread, m_TargetSpread, k);
            ApplyCurls();
        }

        void UpdateTargetPose()
        {
            if (m_HandRoot == null) return;
            Pose palmTarget;
            if (m_Anchor != null && m_HeldWeapon != null) palmTarget = m_Anchor.GetPalmPose(m_IsLeft);
            else
            {
                var euler = m_FreePalmEuler; var off = m_FreePalmOffset;
                if (m_IsLeft) { euler.y = -euler.y; euler.z = -euler.z; off.x = -off.x; }
                palmTarget = new Pose(m_ControllerAttach.TransformPoint(off), m_ControllerAttach.rotation * Quaternion.Euler(euler));
            }
            // hand root pose that puts the palm on the target
            var rootRot = palmTarget.rotation * Quaternion.Inverse(m_PalmLocalRot);
            var rootPos = palmTarget.position - rootRot * Vector3.Scale(m_PalmLocalPos, m_HandRoot.lossyScale);
            float k = 1f - Mathf.Exp(-m_FollowSpeed * Time.deltaTime);
            m_HandRoot.SetPositionAndRotation(Vector3.Lerp(m_HandRoot.position, rootPos, k), Quaternion.Slerp(m_HandRoot.rotation, rootRot, k));
        }

        void ComputeTargetCurls(float grip, float trigger)
        {
            HandPose pose = null; bool holding = m_HeldWeapon != null && m_Anchor != null;
            if (m_OverridePose != null && Time.time < m_OverrideUntil) pose = m_OverridePose;
            else if (holding) pose = m_Anchor.pose != null ? m_Anchor.pose : m_FistPose;
            if (pose != null)
            {
                for (int i = 0; i < 5; i++) m_TargetCurl[i] = pose.Curl(i);
                if (pose.indexFollowsTrigger || holding) m_TargetCurl[1] = Mathf.Lerp(pose.index, Mathf.Max(pose.index, 0.8f), trigger);
                m_TargetSpread = pose.thumbSpread;
                return;
            }
            // free hand: blend idle -> fist by grip, index by trigger
            var open = m_OpenPose; var fist = m_FistPose; var idle = m_IdlePose != null ? m_IdlePose : m_OpenPose;
            for (int i = 0; i < 5; i++)
            {
                float a = idle != null ? idle.Curl(i) : 0.15f, b = fist != null ? fist.Curl(i) : 0.95f;
                m_TargetCurl[i] = Mathf.Lerp(a, b, grip);
            }
            float openIndex = open != null ? open.index : 0f;
            m_TargetCurl[1] = Mathf.Max(m_TargetCurl[1] * (1f - trigger * 0.3f), Mathf.Lerp(openIndex, 0.85f, trigger));
            m_TargetSpread = Mathf.Lerp(idle != null ? idle.thumbSpread : 0.4f, fist != null ? fist.thumbSpread : 0.1f, grip);
        }

        void ApplyCurls()
        {
            if (!m_Calibrated) return;
            for (int f = 0; f < 5; f++)
            {
                var c = m_Fingers[f]; if (c == null) continue;
                float curl = m_Curl[f];
                Transform[] joints = { c.metacarpal, c.proximal, c.intermediate, c.distal };
                float[] max = { f == 0 ? 25f : 0f, m_ProximalMaxDegrees, m_IntermediateMaxDegrees, m_DistalMaxDegrees };
                for (int j = 0; j < 4; j++)
                {
                    var t = joints[j]; if (t == null || max[j] <= 0f) continue;
                    var rot = c.rest[j] * Quaternion.AngleAxis(curl * max[j], c.curlAxisLocal[j]);
                    if (f == 0 && j == 0 && c.spreadAxisLocal.sqrMagnitude > 0f)
                        rot = rot * Quaternion.AngleAxis((m_Spread - 0.5f) * 2f * m_ThumbSpreadMaxDegrees * 0.5f, c.spreadAxisLocal);
                    t.localRotation = rot;
                }
            }
        }

        /// <summary>World position of a fingertip (tests / debugging).</summary>
        public bool TryGetTip(Finger f, out Vector3 world)
        {
            var c = m_Fingers[(int)f];
            if (c == null || c.tip == null) { world = default; return false; }
            world = c.tip.position; return true;
        }

        /// <summary>World position of the palm centre.</summary>
        public Vector3 PalmWorldPosition => m_HandRoot != null ? m_HandRoot.TransformPoint(m_PalmLocalPos) : transform.position;
        public Vector3 PalmInwardWorld => m_HandRoot != null ? -(m_HandRoot.rotation * m_PalmLocalRot * Vector3.up) : -transform.up;
    }
}
