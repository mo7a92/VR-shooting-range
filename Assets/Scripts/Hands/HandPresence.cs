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
        [Tooltip("Thumb bend limits (metacarpal, proximal, distal). The thumb flexes far less than a finger; finger-sized limits fold it into the palm.")]
        [SerializeField] Vector3 m_ThumbMaxDegrees = new Vector3(15f, 45f, 55f);

        [Header("Grip contact solve (fingers close until they touch the weapon's grip collider)")]
        [SerializeField] bool m_ContactSolve = true;
        [Tooltip("Flesh radius around the finger joints/segments (metres): a finger counts as touching when a sample point is this close to the grip collider.")]
        [SerializeField, Range(0.003f, 0.02f)] float m_FingerRadius = 0.0085f;
        [Tooltip("How often the contact curls are re-solved while holding (Hz). Targets are smoothed by Curl Speed in between.")]
        [SerializeField, Range(2f, 60f)] float m_ContactSolveRate = 20f;
        [Tooltip("Log the thumb solve samples (debugging the grip authoring).")]
        [SerializeField] bool m_DebugThumb = false;
        public bool DebugThumb { get => m_DebugThumb; set => m_DebugThumb = value; }

        // joint data
        class JointChain { public Transform metacarpal, proximal, intermediate, distal, tip; public Quaternion[] rest = new Quaternion[4]; public Vector3[] curlAxisLocal = new Vector3[4]; public Vector3 spreadAxisLocal; public Vector3 oppositionAxisLocal; }
        readonly JointChain[] m_Fingers = new JointChain[5];
        Transform m_Wrist, m_Palm;
        Vector3 m_PalmLocalPos; Quaternion m_PalmLocalRot = Quaternion.identity; // palm pose in hand-root space
        bool m_Calibrated;

        // state
        readonly float[] m_Curl = new float[5];
        float m_Spread = 0.4f;
        readonly float[] m_TargetCurl = new float[5];
        float m_TargetSpread;
        // thumb opposition (degrees about the across axis at the metacarpal): + = thumb swings out of the palm, flat along a grip's side
        float m_ThumbOpposition, m_TargetThumbOpposition, m_ContactThumbOpposition;
        Weapon m_HeldWeapon; WeaponGripAnchor m_Anchor; HandPose m_OverridePose; float m_OverrideUntil;
        readonly List<XRBaseInputInteractor> m_Interactors = new List<XRBaseInputInteractor>();
        Renderer[] m_ControllerRenderers;
        float? m_GripOverride, m_TriggerOverride; // test hook (scripted input instead of a tracked controller)
        readonly float[] m_ContactCurl = { -1f, -1f, -1f, -1f, -1f }; // -1 = no contact solution (finger never reaches the grip)
        float m_NextContactSolve;
        Collider m_SolvedAgainst;

        public bool IsLeft => m_IsLeft;
        public Weapon HeldWeapon => m_HeldWeapon;
        public WeaponGripAnchor CurrentAnchor => m_Anchor;
        public bool IsCalibrated => m_Calibrated;
        public float Curl(Finger f) => m_Curl[(int)f];
        public Transform HandRoot => m_HandRoot;
        /// <summary>Curl the contact solve settled on for this finger (-1 when the finger does not reach the grip collider).</summary>
        public float ContactCurl(Finger f) => m_ContactCurl[(int)f];
        public bool ContactSolveEnabled { get => m_ContactSolve; set => m_ContactSolve = value; }

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
            // The thumb opposes the fingers: each thumb joint bends toward the middle finger's middle joint, pushed slightly
            // into the palm, so a grip wraps the thumb across toward the fingers instead of folding it down like a finger.
            var middleInter = Find("MiddleIntermediate") ?? middleProx;
            var thumbGoalPoint = middleInter.position + palmInward * (handSize * 0.15f);
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
                    // Fingers bend toward the palm. The thumb opposes: it bends across the palm toward the little-finger side
                    // (and slightly inward), not straight down like a finger.
                    var inward = palmInward;
                    if (f == 0)
                    {
                        inward = Vector3.ProjectOnPlane(thumbGoalPoint - t.position, dir).normalized;
                        if (inward.sqrMagnitude < 1e-6f) inward = Vector3.ProjectOnPlane(palmInward, dir).normalized;
                    }
                    var axisWorld = Vector3.Cross(dir, inward).normalized;
                    if (axisWorld.sqrMagnitude < 1e-6f) axisWorld = Vector3.Cross(dir, fingerDir).normalized;
                    c.curlAxisLocal[j] = t.InverseTransformDirection(axisWorld);
                }
                if (f == 0 && c.metacarpal != null)
                {
                    c.spreadAxisLocal = c.metacarpal.InverseTransformDirection(-palmInward); // spread = rotate about the back-of-hand normal
                    // opposition = rotate about the thumb-side (across) axis: swings the thumb between "into the palm" and "flat along the side"
                    var across = Vector3.Cross(palmInward, fingerDir).normalized;
                    if (thumbTip != null && Vector3.Dot(across, thumbTip.position - m_Wrist.position) < 0f) across = -across;
                    c.oppositionAxisLocal = c.metacarpal.InverseTransformDirection(across);
                }
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
            // Opposition sign check: a positive opposition must move the thumb tip away from the palm plane (less inward lean).
            var thm = m_Fingers[0];
            if (thm != null && thm.metacarpal != null && thm.tip != null && thm.oppositionAxisLocal.sqrMagnitude > 0f)
            {
                var saved = thm.metacarpal.localRotation;
                float before = Vector3.Dot(thm.tip.position - palmCentre, palmInward);
                thm.metacarpal.localRotation = thm.rest[0] * Quaternion.AngleAxis(15f, thm.oppositionAxisLocal);
                float after = Vector3.Dot(thm.tip.position - palmCentre, palmInward);
                thm.metacarpal.localRotation = saved;
                if (after > before) thm.oppositionAxisLocal = -thm.oppositionAxisLocal;
            }
            // Thumb sign check: bending the thumb must bring its tip closer to the middle finger (opposition).
            var th = m_Fingers[0];
            var goalJoint = middleInter;
            if (th != null && th.proximal != null && th.tip != null)
            {
                var saved = th.proximal.localRotation;
                float before = Vector3.Distance(th.tip.position, goalJoint.position);
                th.proximal.localRotation = th.rest[1] * Quaternion.AngleAxis(20f, th.curlAxisLocal[1]);
                float after = Vector3.Distance(th.tip.position, goalJoint.position);
                th.proximal.localRotation = saved;
                bool flip = after > before;
                if (flip) for (int j = 0; j < 4; j++) th.curlAxisLocal[j] = -th.curlAxisLocal[j];
                Debug.Log($"[VRRange] Hand {(m_IsLeft ? "L" : "R")} thumb: tip-to-middle {before:0.000} -> {after:0.000} m on a 20 deg bend, sign {(flip ? "flipped" : "kept")}");
            }
            // Spread sign check: positive spread must swing the thumb away from the index finger. The left hand is a mirror
            // image, so the same rotation about the back-of-hand normal swings its thumb the other way.
            if (th != null && th.metacarpal != null && th.tip != null && th.spreadAxisLocal.sqrMagnitude > 0f)
            {
                var saved = th.metacarpal.localRotation;
                float before = Vector3.Distance(th.tip.position, indexProx.position);
                th.metacarpal.localRotation = th.rest[0] * Quaternion.AngleAxis(10f, th.spreadAxisLocal);
                float after = Vector3.Distance(th.tip.position, indexProx.position);
                th.metacarpal.localRotation = saved;
                if (after < before) th.spreadAxisLocal = -th.spreadAxisLocal;
                Debug.Log($"[VRRange] Hand {(m_IsLeft ? "L" : "R")} thumb spread sign {(after < before ? "flipped" : "kept")}");
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
            ResetContact();
        }

        void OnWeaponGripChanged(Weapon w)
        {
            if (w != m_HeldWeapon) return;
            var before = m_Anchor;
            foreach (var i in m_Interactors) if (i != null && i.hasSelection) { m_Anchor = w.GetGripAnchorFor(i); if (m_Anchor != null) break; }
            if (m_Anchor != before) ResetContact();
        }

        void ResetContact() { for (int i = 0; i < 5; i++) m_ContactCurl[i] = -1f; m_NextContactSolve = 0f; m_SolvedAgainst = null; m_ContactThumbOpposition = 0f; m_TargetThumbOpposition = 0f; }
        /// <summary>Current thumb opposition in degrees (0 = the rig's relaxed thumb; positive = swung out flat along the grip's side).</summary>
        public float ThumbOpposition => m_ThumbOpposition;

        /// <summary>Temporarily force a pose (e.g. pinch while reloading).</summary>
        public void OverridePose(HandPose pose, float seconds) { m_OverridePose = pose; m_OverrideUntil = Time.time + seconds; }

        /// <summary>Test hook: feed grip/trigger values without a device (null = read the real controller).</summary>
        public void SetInputOverride(float? grip, float? trigger) { m_GripOverride = grip; m_TriggerOverride = trigger; }
        public float TargetCurl(Finger f) => m_TargetCurl[(int)f];

        // ---------- per-frame ----------
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float grip = m_GripOverride ?? HandInput.Grip(m_IsLeft), trigger = m_TriggerOverride ?? HandInput.Trigger(m_IsLeft);
            UpdateTargetPose();
            ComputeTargetCurls(grip, trigger);
            ApplyContactSolve();
            float k = 1f - Mathf.Exp(-m_CurlSpeed * dt);
            for (int i = 0; i < 5; i++) m_Curl[i] = Mathf.Lerp(m_Curl[i], m_TargetCurl[i], k);
            m_Spread = Mathf.Lerp(m_Spread, m_TargetSpread, k);
            m_ThumbOpposition = Mathf.Lerp(m_ThumbOpposition, m_TargetThumbOpposition, k);
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
            for (int f = 0; f < 5; f++) ApplyFingerCurl(f, m_Curl[f], m_Spread, m_ThumbOpposition);
        }

        void ApplyFingerCurl(int f, float curl, float spread, float opposition = 0f)
        {
            var c = m_Fingers[f]; if (c == null) return;
            Transform[] joints = { c.metacarpal, c.proximal, c.intermediate, c.distal };
            float[] max = f == 0
                ? new[] { m_ThumbMaxDegrees.x, m_ThumbMaxDegrees.y, m_ThumbMaxDegrees.y, m_ThumbMaxDegrees.z } // thumb rigs may or may not have an intermediate joint
                : new[] { 0f, m_ProximalMaxDegrees, m_IntermediateMaxDegrees, m_DistalMaxDegrees };
            for (int j = 0; j < 4; j++)
            {
                var t = joints[j]; if (t == null || max[j] <= 0f) continue;
                var rot = c.rest[j] * Quaternion.AngleAxis(curl * max[j], c.curlAxisLocal[j]);
                if (f == 0 && j == 0 && c.spreadAxisLocal.sqrMagnitude > 0f)
                    rot = rot * Quaternion.AngleAxis((spread - 0.5f) * 2f * m_ThumbSpreadMaxDegrees * 0.5f, c.spreadAxisLocal);
                if (f == 0 && j == 0 && opposition != 0f && c.oppositionAxisLocal.sqrMagnitude > 0f)
                    rot = rot * Quaternion.AngleAxis(opposition, c.oppositionAxisLocal);
                t.localRotation = rot;
            }
        }

        // ---------- grip contact solve ----------
        /// <summary>
        /// While a weapon is held, each finger (except the trigger finger, which rests outside the guard and follows the
        /// trigger) closes from the pose's curl until its segments touch the anchor's grip collider, so fingers wrap the
        /// real grip shape and never pass through it. Re-solved at Contact Solve Rate; the pose curl stays the minimum.
        /// </summary>
        void ApplyContactSolve()
        {
            if (!m_ContactSolve || !m_Calibrated) return;
            bool holding = m_HeldWeapon != null && m_Anchor != null && m_Anchor.gripCollider != null && m_Anchor.gripCollider.enabled;
            if (!holding || (m_OverridePose != null && Time.time < m_OverrideUntil)) { if (m_SolvedAgainst != null) ResetContact(); return; }
            var grip = m_Anchor.gripCollider;
            if (Time.time >= m_NextContactSolve || m_SolvedAgainst != grip)
            {
                m_NextContactSolve = Time.time + 1f / Mathf.Max(1f, m_ContactSolveRate);
                m_SolvedAgainst = grip;
                bool indexFree = m_Anchor.pose == null || m_Anchor.pose.indexFollowsTrigger;
                bool shootingHand = m_Anchor.role == GripRole.Primary;
                for (int f = 0; f < 5; f++)
                {
                    if (f == 1 && indexFree) { m_ContactCurl[f] = -1f; continue; }
                    if (m_Fingers[f] == null) { m_ContactCurl[f] = -1f; continue; }
                    m_ContactCurl[f] = f == 0 && shootingHand ? SolveThumbOnPalmSide(m_TargetCurl[f], grip) : SolveFinger(f, m_TargetCurl[f], grip);
                }
                if (!shootingHand) m_ContactThumbOpposition = 0f;
                // the solve posed the joints; ApplyCurls below restores the smoothed state
            }
            for (int f = 0; f < 5; f++) if (m_ContactCurl[f] >= 0f) m_TargetCurl[f] = m_ContactCurl[f];
            m_TargetThumbOpposition = m_ContactThumbOpposition;
        }

        /// <summary>Largest curl in [0,1] at which the finger touches the collider without penetrating it, starting at minCurl; -1 if it never touches.</summary>
        float SolveFinger(int f, float minCurl, Collider grip)
        {
            const float coarse = 0.05f;
            float spread = m_TargetSpread;
            int state = Contact(f, minCurl, spread, grip);
            if (state == 2)
            {
                // the pre-shape already pushes into the grip: open the finger until it is clear (then it rests on the surface);
                // if even a straight finger is inside, the palm is misplaced and the pose curl is the least-bad result
                for (float c = minCurl - coarse; c >= -1e-4f; c -= coarse)
                {
                    int s = Contact(f, Mathf.Max(0f, c), spread, grip);
                    if (s != 2) return Mathf.Max(0f, c);
                }
                return minCurl;
            }
            if (state == 1) return minCurl;
            float lo = minCurl;
            // the thumb only closes a little past its pre-shape: a thumb that sweeps right across the grip ends on the fingers' side
            float maxCurl = f == 0 ? Mathf.Min(1f, minCurl + 0.35f) : 1f;
            for (float c = minCurl + coarse; c <= maxCurl + 1e-4f; c += coarse)
            {
                float cc = Mathf.Min(maxCurl, c);
                int s = Contact(f, cc, spread, grip);
                if (s == 0) { lo = cc; continue; }
                if (s == 1) return cc;
                // penetrating between lo (free) and cc: bisect to the contact curl
                float hi = cc;
                for (int i = 0; i < 4; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    int sm = Contact(f, mid, spread, grip);
                    if (sm == 2) hi = mid; else { lo = mid; if (sm == 1) return mid; }
                }
                return lo;
            }
            return -1f;
        }

        /// <summary>
        /// Shooting-hand thumb: it must lie along the frame on the palm's side of the grip (never cross over the top to the
        /// fingers' side). The rig's relaxed thumb is already half opposed, so the search starts from an extended thumb
        /// (negative curl, i.e. unbending the rig's rest bend) and closes it until it touches the grip, stopping before the tip crosses the grip's centre plane.
        /// </summary>
        float SolveThumbOnPalmSide(float minCurl, Collider grip)
        {
            var c = m_Fingers[0]; if (c == null || c.tip == null) { m_ContactThumbOpposition = 0f; return -1f; }
            float spread = m_TargetSpread;
            float sideSign = Mathf.Sign(grip.transform.InverseTransformPoint(PalmWorldPosition).x);
            float TipSide() => grip.transform.InverseTransformPoint(c.tip.position).x * sideSign;
            // 1. opposition: swing the relaxed (inward-leaning) thumb out of the palm until its tip is on the palm's side of the grip
            float opposition = -1f;
            for (float o = 0f; o <= 75f + 1e-3f; o += 5f)
            {
                ApplyFingerCurl(0, minCurl, spread, o);
                if (TipSide() > 0.004f) { opposition = o; break; }
            }
            if (m_DebugThumb)
            {
                var sb = new System.Text.StringBuilder($"[VRRange] thumb solve {(m_IsLeft ? "L" : "R")} on {grip.name}: palm x {grip.transform.InverseTransformPoint(PalmWorldPosition).x * 1000f:0} mm, opposition {opposition:0};");
                foreach (var test in new[] { 0f, 30f, 60f }) { ApplyFingerCurl(0, minCurl, spread, test); var tl = grip.transform.InverseTransformPoint(c.tip.position) * 1000f; sb.Append($" opp {test:0}: tip ({tl.x:0},{tl.y:0},{tl.z:0});"); }
                Debug.Log(sb.ToString());
            }
            if (opposition < 0f) { m_ContactThumbOpposition = 0f; return minCurl; }
            m_ContactThumbOpposition = opposition;
            // 2. with that opposition, close the thumb from its pre-shape until it touches the grip side, never crossing over
            float maxCurl = Mathf.Min(1f, minCurl + 0.5f);
            float lastValid = float.NaN;
            for (float curl = Mathf.Max(0f, minCurl - 0.3f); curl <= maxCurl + 1e-4f; curl += 0.05f)
            {
                ApplyFingerCurl(0, curl, spread, opposition);
                int state = ContactState(0, grip);
                bool valid = TipSide() > 0.002f && state != 2;
                if (!valid) { if (!float.IsNaN(lastValid)) break; continue; }
                lastValid = curl;
                if (state == 1) return curl;
            }
            return float.IsNaN(lastValid) ? minCurl : lastValid;
        }

        /// <summary>
        /// 0 = clear of the grip, 1 = touching (within the flesh radius), 2 = a segment point is inside the collider by more
        /// than 1 mm. The knuckle (proximal joint) itself is not sampled: it does not move with the curl, so it must not
        /// drive the search; the samples are the segment midpoints, the middle/end joints and the tip.
        /// </summary>
        int Contact(int f, float curl, float spread, Collider grip)
        {
            ApplyFingerCurl(f, curl, spread, f == 0 ? m_ContactThumbOpposition : 0f);
            return ContactState(f, grip);
        }

        int ContactState(int f, Collider grip)
        {
            var c = m_Fingers[f];
            int result = 0;
            Transform[] pts = { c.proximal, c.intermediate, c.distal, c.tip };
            Vector3 prev = default; bool hasPrev = false;
            for (int i = 0; i < 4; i++)
            {
                var t = pts[i]; if (t == null) continue;
                var p = t.position;
                if (hasPrev)
                {
                    int s = Sample(Vector3.Lerp(prev, p, 0.6f), grip); if (s > result) result = s;
                }
                if (i > 0) { int s = Sample(p, grip); if (s > result) result = s; }
                prev = p; hasPrev = true;
                if (result == 2) return 2;
            }
            return result;
        }

        int Sample(Vector3 p, Collider grip)
        {
            if (grip is BoxCollider box)
            {
                // exact inside test for the box grips (ClosestPoint returns the point itself both inside and on the surface)
                var l = box.transform.InverseTransformPoint(p) - box.center; var h = box.size * 0.5f;
                float depth = Mathf.Min(h.x - Mathf.Abs(l.x), Mathf.Min(h.y - Mathf.Abs(l.y), h.z - Mathf.Abs(l.z)));
                if (depth > 0.001f) return 2;
                if (depth >= 0f) return 1;
            }
            var q = grip.ClosestPoint(p);
            float d = Vector3.Distance(p, q);
            if (d < 0.0005f) return grip is BoxCollider ? 1 : 2;
            return d < m_FingerRadius ? 1 : 0;
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
