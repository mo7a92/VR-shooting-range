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
        // not serialized on purpose: the committed scene carries older values and the pose is tuned with the weapon anchors in code
        Vector3 m_FreePalmOffset = DefaultFreePalmOffset;
        Vector3 m_FreePalmEuler = DefaultFreePalmEuler;
        /// <summary>
        /// Free-hand palm pose relative to the controller's grip pose. OpenXR defines the grip pose as: origin at the palm
        /// centroid, +X = palm normal, +Y along the handle, forward (Unity +Z) out of the fist perpendicular to the handle,
        /// i.e. the direction a pistol barrel points when the handle is gripped. A fist around the handle therefore has its
        /// wrist-to-knuckle line a little below that forward and the back of the hand facing outward and a bit up: the same
        /// orientation as the shooting hand on a pistol grip. The weapon anchors use these exact vectors (authored for a
        /// right hand, mirrored in X for the left), so the held wrist lands where the free wrist was.
        /// </summary>
        public static readonly Vector3 DefaultFreePalmOffset = Vector3.zero;
        public static readonly Vector3 DefaultFreePalmForward = new Vector3(-0.08f, -0.37f, 0.93f);
        public static readonly Vector3 DefaultFreePalmUp = new Vector3(0.93f, 0.3f, 0.2f);
        public static readonly Vector3 DefaultFreePalmEuler = Quaternion.LookRotation(DefaultFreePalmForward, DefaultFreePalmUp).eulerAngles;
        public Vector3 FreePalmOffset => m_FreePalmOffset;
        public Vector3 FreePalmEuler => m_FreePalmEuler;
        /// <summary>Where the free (empty) hand's palm would be for the current controller pose.</summary>
        public Pose FreePalmPose()
        {
            var fwd = DefaultFreePalmForward; var up = DefaultFreePalmUp; var off = m_FreePalmOffset;
            if (m_IsLeft) { fwd.x = -fwd.x; up.x = -up.x; off.x = -off.x; }
            var attach = m_PoseReference != null ? m_PoseReference : m_ControllerAttach != null ? m_ControllerAttach : transform;
            return new Pose(attach.TransformPoint(off), attach.rotation * Quaternion.LookRotation(fwd, up));
        }
        /// <summary>World wrist position the hand would have in a given palm pose (bone-line palm centre).</summary>
        public Vector3 WristForPalmPose(Pose palm)
        {
            var rootRot = palm.rotation * Quaternion.Inverse(m_PalmLocalRot);
            var rootPos = palm.position - rootRot * Vector3.Scale(m_PalmLocalPos, m_HandRoot != null ? m_HandRoot.lossyScale : Vector3.one);
            var wristLocal = m_Wrist != null && m_HandRoot != null ? m_HandRoot.InverseTransformPoint(m_Wrist.position) : Vector3.zero;
            return rootPos + rootRot * Vector3.Scale(wristLocal, m_HandRoot != null ? m_HandRoot.lossyScale : Vector3.one);
        }

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
        [Tooltip("Push the whole hand out of the weapon's contact volumes (and the other hand's) with Physics.ComputePenetration on the palm volume.")]
        [SerializeField] bool m_PalmDepenetration = true;

        // measured at calibration from the baked skin: distance from the palm bone line to the palm skin / back-of-hand skin
        float m_PalmSkinOffset = 0.018f, m_BackOfHandOffset = 0.012f;
        // contact volumes of this hand (palm box + finger capsules, parented to the joints) and the current contact targets
        readonly List<Collider> m_Targets = new List<Collider>();
        readonly List<ContactShape> m_Shapes = new List<ContactShape>();
        int m_OtherHandShapeStart = -1;
        HandPresence m_OtherHand;
        /// <summary>Everything this hand keeps its palm and fingers out of: the weapon's contact shapes, then (support hand) the other hand's shapes inflated by <see cref="kOtherHandInflate"/>.</summary>
        public IReadOnlyList<ContactShape> ContactShapes => m_Shapes;
        readonly List<string> m_ShapeNames = new List<string>();
        /// <summary>Names parallel to <see cref="ContactShapes"/> (weapon volume collider names, then "other:" + the other hand's shape names).</summary>
        public IReadOnlyList<string> ContactShapeNames => m_ShapeNames;
        /// <summary>
        /// The other hand's capsules, knuckle spheres and palm box are inflated by 3 mm (skin-on-skin contact must not read as
        /// overlap). 7 mm held the pistol support palm 3-4 cm off the shooting hand's fingers; 3 mm without knuckle spheres let
        /// it sink 6.5 mm into the index knuckle, hence the spheres.
        /// </summary>
        const float kOtherHandInflate = 0.003f;
        void RefreshShapes()
        {
            m_Shapes.Clear(); m_ShapeNames.Clear(); m_OtherHandShapeStart = -1;
            foreach (var c in m_Targets) if (c != null) { m_Shapes.Add(ContactShape.From(c)); m_ShapeNames.Add(c.name); }
            if (m_OtherHand != null)
            {
                m_OtherHandShapeStart = m_Shapes.Count;
                m_OtherHand.RefreshOwnShapes();
                var names = m_OtherHand.OwnShapeNames;
                int k = 0;
                foreach (var sh0 in m_OtherHand.OwnShapes)
                {
                    var sh = sh0;
                    if (sh.kind == 1 || sh.kind == 2) sh.radius += kOtherHandInflate; else if (sh.kind == 0) sh.half += Vector3.one * kOtherHandInflate;
                    m_Shapes.Add(sh); m_ShapeNames.Add("other:" + (k < names.Count ? names[k] : k.ToString())); k++;
                }
            }
        }
        /// <summary>Human-readable summary of the last palm de-penetration solve (tests / debugging).</summary>
        public string DepenetrationInfo { get; private set; } = "";
        Vector3 m_DepenOffset, m_DepenTarget;
        Transform m_MiddleProx;
        // the frame the free pose is authored in: the controller's NearFarInteractor attach anchor (where a held weapon's
        // palm attach lands), falling back to the controller transform
        Transform m_PoseReference;
        public Transform PoseReference => m_PoseReference != null ? m_PoseReference : m_ControllerAttach;

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
        // extra closing of the end joints (0..1) after the finger's first contact, so the fingertip reaches the surface too
        readonly float[] m_TipExtra = new float[5], m_TargetTipExtra = new float[5], m_ContactTipExtra = new float[5];
        public float TipExtra(Finger f) => m_TipExtra[(int)f];
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
            foreach (var i in m_Interactors) if (i is NearFarInteractor nf) { var a = nf.attachTransform; if (a != null) { m_PoseReference = a; break; } }
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
        /// <summary>
        /// The XR Hands sample mesh is a fat, slightly large hand: 39 mm thick at the palm (22 mm palm skin + 17 mm back of hand) and
        /// 96 mm wrist to middle knuckle, where an adult hand is about 28 mm and 88 mm. Its thenar and web are 10-17 mm thicker than
        /// the palm centre, which held every palm 15-20 mm off a grip panel (the de-penetration backs the whole rigid hand out of the
        /// frame overhang). The root is scaled once at calibration to adult proportions: 0.72 across the palm (hand-root Y = palm
        /// normal), 0.92 along and across the fingers. Every measurement below (skin, finger radius, palm cloud) is taken after it.
        /// </summary>
        static readonly Vector3 kProportionScale = new Vector3(0.92f, 0.72f, 0.92f);
        bool m_ProportionsApplied;

        public void Calibrate()
        {
            m_Calibrated = false;
            if (m_HandRoot == null) return;
            if (!m_ProportionsApplied) { m_HandRoot.localScale = Vector3.Scale(m_HandRoot.localScale, kProportionScale); m_ProportionsApplied = true; }
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
            m_MiddleProx = middleProx;
            MeasureSkinOffsets(palmCentre, palmInward, fingerDir, handSize);
            MeasureFingerRadius(Find("MiddleIntermediate"), Find("MiddleDistal"));
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
            BuildContactVolumes(indexProx, littleProx, handSize);
            CachePalmCloud();
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

        void ResetContact() { for (int i = 0; i < 5; i++) { m_ContactCurl[i] = -1f; m_ContactTipExtra[i] = 0f; m_TargetTipExtra[i] = 0f; } m_NextContactSolve = 0f; m_SolvedAgainst = null; m_ContactThumbOpposition = 0f; m_TargetThumbOpposition = 0f; m_Targets.Clear(); m_OtherHand = null; m_DepenTarget = Vector3.zero; }
        /// <summary>Current thumb opposition in degrees (0 = the rig's relaxed thumb; positive = swung out flat along the grip's side).</summary>
        public float ThumbOpposition => m_ThumbOpposition;
        /// <summary>Distance from the palm bone line to the palm skin, measured from the baked mesh at calibration (metres).</summary>
        public float PalmSkinOffset => m_PalmSkinOffset;
        public float BackOfHandOffset => m_BackOfHandOffset;
        /// <summary>Colliders the fingers and palm are currently kept out of (weapon contact volumes, other hand).</summary>
        public IReadOnlyList<Collider> ContactTargets => m_Targets;
        /// <summary>World offset currently applied to the hand root by the palm de-penetration step.</summary>
        public Vector3 DepenetrationOffset => m_DepenOffset;
        public Vector3 WristWorldPosition => m_Wrist != null ? m_Wrist.position : transform.position;
        public Vector3 MiddleKnuckleWorldPosition => m_MiddleProx != null ? m_MiddleProx.position : transform.position;
        /// <summary>World point where the palm skin touches a grip (bone-line centre pushed out by the measured skin thickness).</summary>
        public Vector3 PalmSkinWorldPosition => PalmWorldPosition + PalmInwardWorld * m_PalmSkinOffset;
        public Vector3 BackOfHandWorld => -PalmInwardWorld;

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
            for (int i = 0; i < 5; i++) m_TipExtra[i] = Mathf.Lerp(m_TipExtra[i], m_TargetTipExtra[i], k);
            ApplyCurls();
        }

        void UpdateTargetPose()
        {
            if (m_HandRoot == null) return;
            Pose palmTarget;
            if (m_Anchor != null && m_HeldWeapon != null)
            {
                // the anchor is the point where the palm SKIN touches the grip: the bone-line palm centre sits one measured skin
                // thickness further out (along the anchor's up = back of hand), plus whatever the palm de-penetration pushed
                palmTarget = m_Anchor.GetPalmPose(m_IsLeft);
                palmTarget.position += palmTarget.rotation * Vector3.up * m_PalmSkinOffset + m_DepenOffset;
            }
            else palmTarget = FreePalmPose();
            // hand root pose that puts the palm on the target
            var rootRot = palmTarget.rotation * Quaternion.Inverse(m_PalmLocalRot);
            var rootPos = palmTarget.position - rootRot * Vector3.Scale(m_PalmLocalPos, m_HandRoot.lossyScale);
            float k = 1f - Mathf.Exp(-m_FollowSpeed * Time.deltaTime);
            m_HandRoot.SetPositionAndRotation(Vector3.Lerp(m_HandRoot.position, rootPos, k), Quaternion.Slerp(m_HandRoot.rotation, rootRot, k));
            m_DepenOffset = Vector3.Lerp(m_DepenOffset, m_DepenTarget, k);
        }

        // ---------- skin measurement and contact volumes ----------
        /// <summary>
        /// Measures how far the palm skin and the back-of-hand skin sit from the palm bone line, from the skinned mesh baked
        /// in the rest pose (80th percentile of the vertices within 2 cm of the palm centre in the palm plane). Per hand, so a
        /// different hand model or scale is handled without a magic number.
        /// </summary>
        void MeasureSkinOffsets(Vector3 palmCentre, Vector3 palmInward, Vector3 fingerDir, float handSize)
        {
            var smr = m_HandRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) return;
            var verts = new List<Vector3>();
            if (!BakeWorldVertices(smr, verts) || verts.Count == 0) return;
            var inward = new List<float>(); var outward = new List<float>();
            // a narrow column through the palm centre (the thumb ball and finger bases would inflate the measurement)
            float radius = Mathf.Clamp(handSize * 0.15f, 0.008f, 0.02f);
            foreach (var v in verts)
            {
                var d = v - palmCentre;
                float along = Vector3.Dot(d, palmInward);
                var inPlane = d - palmInward * along;
                if (inPlane.magnitude > radius) continue;
                if (along > 0f) inward.Add(along); else outward.Add(-along);
            }
            // the skin is the median of the surface samples on each side (the column contains only surface vertices)
            if (inward.Count >= 6) { inward.Sort(); m_PalmSkinOffset = Mathf.Clamp(inward[inward.Count / 2], 0.006f, 0.04f); }
            if (outward.Count >= 6) { outward.Sort(); m_BackOfHandOffset = Mathf.Clamp(outward[outward.Count / 2], 0.004f, 0.03f); }
            Debug.Log($"[VRRange] Hand {(m_IsLeft ? "L" : "R")} skin: palm {m_PalmSkinOffset * 1000f:0.0} mm, back of hand {m_BackOfHandOffset * 1000f:0.0} mm from the bone line ({inward.Count}/{outward.Count} samples)");
        }

        /// <summary>Flesh radius of the fingers from the baked mesh: median distance of the vertices around the middle finger's middle segment to its bone line.</summary>
        void MeasureFingerRadius(Transform a, Transform b)
        {
            if (a == null || b == null) return;
            var smr = m_HandRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var verts = new List<Vector3>();
            if (smr == null || !BakeWorldVertices(smr, verts)) return;
            var ab = b.position - a.position; float len = ab.magnitude; if (len < 1e-4f) return;
            var dir = ab / len; var radii = new List<float>();
            foreach (var v in verts)
            {
                float t = Vector3.Dot(v - a.position, dir);
                if (t < len * 0.25f || t > len * 0.75f) continue; // the middle of the segment only (no joints)
                float r = Vector3.Distance(v, a.position + dir * t);
                if (r < 0.02f) radii.Add(r);
            }
            if (radii.Count < 6) return;
            radii.Sort();
            m_FingerRadius = Mathf.Clamp(radii[radii.Count / 2], 0.005f, 0.013f);
            Debug.Log($"[VRRange] Hand {(m_IsLeft ? "L" : "R")} finger radius {m_FingerRadius * 1000f:0.0} mm ({radii.Count} samples)");
        }

        /// <summary>Baked skinned-mesh vertices in world space. Handles both BakeMesh scale conventions by comparing with the renderer bounds.</summary>
        public static bool BakeWorldVertices(SkinnedMeshRenderer smr, List<Vector3> world)
        {
            world.Clear();
            if (smr == null || smr.sharedMesh == null) return false;
            var mesh = new Mesh();
            // useScale = true bakes into the renderer's local space including its (lossy) scale, so the exact inverse is the
            // renderer's full local-to-world transform; this also holds under the non-uniform proportion scale on the hand root
            smr.BakeMesh(mesh, true);
            var local = mesh.vertices;
            var t = smr.transform;
            for (int i = 0; i < local.Length; i++) world.Add(t.TransformPoint(local[i]));
            Object.Destroy(mesh);
            return true;
        }

        // palm box dimensions measured at calibration (metres); the shapes themselves are rebuilt from the joints every solve
        float m_PalmWidth, m_PalmLength;
        readonly List<ContactShape> m_OwnShapes = new List<ContactShape>();
        readonly List<string> m_OwnShapeNames = new List<string>();
        /// <summary>This hand's contact shapes (palm box + one capsule per finger segment), as of its last refresh.</summary>
        public IReadOnlyList<ContactShape> OwnShapes => m_OwnShapes;
        public IReadOnlyList<string> OwnShapeNames => m_OwnShapeNames;

        void BuildContactVolumes(Transform indexProx, Transform littleProx, float handSize)
        {
            // The palm volume is the fleshy inward half of the palm only (bone line to palm skin), 70 % of the knuckle span
            // wide and 60 % of the wrist-to-knuckle length long: a real palm conforms to a grip, so a full rigid box of the
            // hand's outline would push the hand away as soon as it is tilted against a flat grip panel.
            m_PalmWidth = Vector3.Distance(indexProx.position, littleProx.position) * 0.7f;
            m_PalmLength = handSize * 0.6f;
            RefreshOwnShapes();
        }

        /// <summary>Rebuilds the palm box and finger capsules from the current joint poses (pure geometry, no physics objects).</summary>
        public void RefreshOwnShapes()
        {
            m_OwnShapes.Clear(); m_OwnShapeNames.Clear();
            if (m_HandRoot == null || !m_Calibrated) return;
            var palmRot = m_HandRoot.rotation * m_PalmLocalRot;
            var palm = new ContactShape { kind = 0, scale = 1f, rot = palmRot, invRot = Quaternion.Inverse(palmRot), half = new Vector3(m_PalmWidth, m_PalmSkinOffset, m_PalmLength) * 0.5f, center = PalmWorldPosition + palmRot * (Vector3.down * (0.5f * m_PalmSkinOffset)) };
            m_OwnShapes.Add(palm); m_OwnShapeNames.Add("Palm");
            for (int f = 0; f < 5; f++)
            {
                var c = m_Fingers[f]; if (c == null) continue;
                Transform[] pts = { c.proximal, c.intermediate, c.distal, c.tip };
                for (int i = 0; i < 3; i++)
                {
                    var a = pts[i]; var b = pts[i + 1]; if (a == null) continue; if (b == null) b = pts[3];
                    if (b == null) continue;
                    m_OwnShapes.Add(new ContactShape { kind = 1, scale = 1f, a = a.position, b = b.position, radius = f == 0 ? m_FingerRadius * 1.1f : m_FingerRadius });
                    m_OwnShapeNames.Add($"Seg{f}{i}");
                }
                // the knuckles (middle and end joints) are thicker than the segments between them: a sphere of 1.3 finger radii
                // at each, so the other hand's palm does not sink into them (the capsules alone under-read them by ~6 mm)
                for (int i = 1; i < 3; i++)
                {
                    var j = pts[i]; if (j == null) continue;
                    m_OwnShapes.Add(new ContactShape { kind = 2, scale = 1f, center = j.position, radius = m_FingerRadius * (f == 0 ? 1.4f : 1.3f) });
                    m_OwnShapeNames.Add($"Knu{f}{i}");
                }
            }
        }

        /// <summary>Rebuilds the contact target list: the weapon's contact volumes (or its grip collider) plus, for a support hand, the other hand's volumes.</summary>
        void RefreshTargets()
        {
            m_Targets.Clear();
            if (m_HeldWeapon == null || m_Anchor == null) return;
            var wcv = m_HeldWeapon.GetComponent<WeaponContactVolumes>();
            if (wcv != null && wcv.volumes != null) foreach (var c in wcv.volumes) if (c != null && c.enabled) m_Targets.Add(c);
            if (m_Targets.Count == 0 && m_Anchor.gripCollider != null) m_Targets.Add(m_Anchor.gripCollider);
            // both hands know each other: the support hand's palm backs out of the shooting hand, and the shooting hand's
            // fingers stop at the support hand's thumb / palm instead of being solved as if it were not there
            m_OtherHand = null;
            foreach (var h in FindObjectsByType<HandPresence>(FindObjectsSortMode.None))
                if (h != this && h.HeldWeapon == m_HeldWeapon && h.m_HeldWeapon != null) { m_OtherHand = h; break; }
        }

        /// <summary>
        /// Palm de-penetration on the real skin: the skinned mesh is baked (at the solve rate), the palm / thenar / knuckle /
        /// wrist vertices (fingers and thumb excluded: the curl solve owns those) are tested against every contact target,
        /// and the hand root is pushed out along the deepest vertex's surface normal, a few iterations, so the palm skin
        /// rests on grip panels and rails instead of sinking into them.
        /// </summary>
        // Palm / thenar / knuckle-row / wrist vertices (hand-root space), baked once in the rest pose: that region is rigid with
        // respect to the hand root, so no skinned-mesh bake is needed at runtime (a BakeMesh inside LateUpdate stalled the editor).
        readonly List<Vector3> m_PalmCloudLocal = new List<Vector3>();
        /// <summary>
        /// Per cloud vertex: how far it may sink into a contact shape (metres). The thenar and hypothenar bulges stand 10-17 mm
        /// proud of the palm centre on this rigid mesh and the side ridges of the palm squash under a frame or receiver overhang; a real
        /// hand squashes them against the grip instead of holding the whole palm off the panel, so a vertex may compress by its
        /// thickness beyond the palm-centre skin (plus 8 mm on the side ridges), up to <see cref="kMaxBulgeCompression"/>
        /// (and only <see cref="kMaxCrossHandCompression"/> into the other hand, where the overlap would show).
        /// </summary>
        readonly List<float> m_PalmCloudExcess = new List<float>();
        const float kMaxBulgeCompression = 0.012f, kMaxCrossHandCompression = 0.003f;
        readonly List<Vector3> m_BakeBuffer = new List<Vector3>();
        void CachePalmCloud()
        {
            m_PalmCloudLocal.Clear(); m_PalmCloudExcess.Clear();
            var smr = m_HandRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null || !BakeWorldVertices(smr, m_BakeBuffer)) return;
            RefreshOwnShapes();
            // Vertices of the digits beyond the knuckles (middle and end segments of every finger and of the thumb) belong
            // to the curl / opposition solve; everything else (palm, thenar, knuckle row, wrist) is cached here.
            var digitShapes = new List<ContactShape>();
            for (int k = 0; k < m_OwnShapes.Count; k++) if (m_OwnShapeNames[k].StartsWith("Seg") && !m_OwnShapeNames[k].EndsWith("0")) digitShapes.Add(m_OwnShapes[k]);
            float handLen = m_MiddleProx != null ? Vector3.Distance(WristWorldPosition, m_MiddleProx.position) : 0.09f;
            var fingerDirRest = m_MiddleProx != null ? (m_MiddleProx.position - WristWorldPosition).normalized : m_HandRoot.forward;
            for (int i = 0; i < m_BakeBuffer.Count; i += 2)
            {
                var v = m_BakeBuffer[i];
                bool digit = false;
                for (int k = 0; k < digitShapes.Count && !digit; k++) if (SignedDistance(digitShapes[k], v) < 0.004f) digit = true;
                if (digit) continue;
                m_PalmCloudLocal.Add(m_HandRoot.InverseTransformPoint(v));
                float thickness = Vector3.Dot(v - PalmWorldPosition, PalmInwardWorld); // below the bone plane, towards the palm skin
                // the thumb-side and little-side ridges of the palm (web of the hand, thenar, hypothenar: more than 30 % of the hand
                // length off the palm centre line) are soft and squash under a frame / receiver overhang; they get 8 mm on top of
                // their bulge excess. The palm centre strip gets its bulge excess only (none at the palm centre itself).
                float across = Mathf.Abs(Vector3.Dot(v - PalmWorldPosition, Vector3.Cross(PalmInwardWorld, (m_MiddleProx != null ? m_MiddleProx.position : PalmWorldPosition + m_HandRoot.forward) - PalmWorldPosition).normalized));
                // ramps from 0 at the palm centre line to 8 mm at 30 % of the hand length: a palm pressed on a flat panel at the
                // grip-pose roll (18 deg) touches along a line and its pressing edge squashes a few mm
                float ridge = 0.008f * Mathf.Clamp01(across / (handLen * 0.3f));
                // only the palm proper may compress: the knuckle row / proximal finger segments (beyond 55 % of the hand length)
                // and the wrist (behind 45 %) hang below the palm plane in the rest pose and must not inherit a "bulge" allowance
                float along = Vector3.Dot(v - PalmWorldPosition, fingerDirRest);
                bool palmProper = along <= handLen * 0.55f && along >= -handLen * 0.45f;
                m_PalmCloudExcess.Add(palmProper ? Mathf.Clamp(Mathf.Max(0f, thickness - m_PalmSkinOffset) + ridge, 0f, kMaxBulgeCompression) : 0f);
            }
            Debug.Log($"[VRRange] Hand {(m_IsLeft ? "L" : "R")} palm cloud: {m_PalmCloudLocal.Count} vertices cached for de-penetration");
        }

        void SolvePalmDepenetration()
        {
            if (!m_PalmDepenetration || m_PalmCloudLocal.Count == 0) { m_DepenTarget = Vector3.zero; return; }
            // The solve works on the hand as it is posed WITHOUT the current de-penetration offset, so the result is absolute, not accumulated.
            var outward = -PalmInwardWorld; // back of hand
            var l2w = m_HandRoot.localToWorldMatrix;
            m_BakeBuffer.Clear();
            for (int i = 0; i < m_PalmCloudLocal.Count; i++) m_BakeBuffer.Add(l2w.MultiplyPoint3x4(m_PalmCloudLocal[i]) - m_DepenOffset);
            // Required outward travel per vertex. A vertex counts when it entered through a face roughly facing the palm
            // (normal within ~70 deg): wrap-around contact at a grip's front or side is the finger solve's job, and pushing the
            // hand out by a side face's full exit distance would lift the palm off the grip. The travel for a counted vertex
            // is depth / cos(angle), capped so one stray vertex cannot throw the hand.
            float Travel(Vector3 v, float excess, out int shape, out float depthOut)
            {
                float best = 0f; shape = -1; depthOut = 0f;
                for (int si = 0; si < m_Shapes.Count; si++)
                {
                    float depth = DepthAndNormal(m_Shapes[si], v, out var n);
                    // a bulge vertex may compress by its excess thickness (less into the other hand)
                    // (a support hand presses on rails and handguard edges, not on a panel under an overhang: 3 mm there too)
                    bool crossHand = si >= m_OtherHandShapeStart && m_OtherHandShapeStart >= 0;
                    if (crossHand && m_Anchor != null && m_Anchor.role == GripRole.Primary) continue; // the shooting hand never moves for the support hand
                    depth -= crossHand || (m_Anchor != null && m_Anchor.role != GripRole.Primary) ? Mathf.Min(excess, kMaxCrossHandCompression) : excess;
                    if (depth <= 0f) continue;
                    float align = Vector3.Dot(n, outward);
                    // exact travel along the push direction to leave the shape (a corner vertex near the back strap must not be
                    // charged the full depth-over-cosine of the side face), minus the travel the allowed compression already covers;
                    // side-entered vertices (face normal > ~70 deg from the palm normal) are bounded by twice their depth so
                    // wrap-around contact cannot lift the palm off the grip
                    float exitAlong = Mathf.Max(0f, ExitDistance(m_Shapes[si], v, outward) - (crossHand || (m_Anchor != null && m_Anchor.role != GripRole.Primary) ? Mathf.Min(excess, kMaxCrossHandCompression) : excess) / Mathf.Max(align, 0.5f));
                    float e = Mathf.Min(exitAlong, align >= 0.35f ? depth / align : depth * 2f, 0.03f);
                    if (e > best) { best = e; shape = si; depthOut = depth; }
                }
                return best;
            }
            float exit = 0f; int exitShape = -1; float exitDepth = 0f, exitExcess = 0f; Vector3 exitVertexLocal = Vector3.zero;
            for (int i = 0; i < m_BakeBuffer.Count; i++)
            {
                float e = Travel(m_BakeBuffer[i], m_PalmCloudExcess[i], out int sh, out float d);
                if (e > exit) { exit = e; exitShape = sh; exitDepth = d; exitVertexLocal = m_PalmCloudLocal[i]; exitExcess = m_PalmCloudExcess[i]; }
            }
            // 1.5 mm clearance: the cached palm cloud is rigid, the live thenar moves a little with the thumb opposition
            var push = outward * Mathf.Min(exit > 0f ? exit + 0.0015f : 0f, 0.06f);
            string info = exitShape >= 0
                ? $"pass1 {exit * 1000f:0.0} mm along the palm normal (vertex at hand-local {exitVertexLocal * 1000f:0} mm, {exitDepth * 1000f:0.0} mm deep after its {exitExcess * 1000f:0.0} mm allowance in {(exitShape < m_ShapeNames.Count ? m_ShapeNames[exitShape] : exitShape.ToString())})"
                : "pass1 0 mm";
            float pass2 = 0f; string pass2Shape = "";
            // Second pass for the other hand only (support hand cupping the shooting hand): its finger capsules are round, so
            // the hand backs out of the deepest one along that capsule's radial normal, a few iterations.
            if (m_OtherHandShapeStart >= 0 && m_Anchor != null && m_Anchor.role != GripRole.Primary)
            {
                for (int iter = 0; iter < 3; iter++)
                {
                    float deepest = 0f; var normal = Vector3.zero; int deepestShape = -1;
                    for (int i = 0; i < m_BakeBuffer.Count; i++)
                    {
                        var v = m_BakeBuffer[i] + push;
                        for (int si = m_OtherHandShapeStart; si < m_Shapes.Count; si++)
                        {
                            float depth = DepthAndNormal(m_Shapes[si], v, out var n) - Mathf.Min(m_PalmCloudExcess[i], kMaxCrossHandCompression);
                            if (depth > deepest) { deepest = depth; normal = n; deepestShape = si; }
                        }
                    }
                    if (deepest < 0.001f) break;
                    float step = Mathf.Min(deepest + 0.0005f, 0.015f);
                    push += normal * step; pass2 += step;
                    if (deepestShape >= 0 && deepestShape < m_ShapeNames.Count) pass2Shape = m_ShapeNames[deepestShape];
                }
            }
            if (pass2 > 0f) info += $", pass2 {pass2 * 1000f:0.0} mm radially out of {pass2Shape}";
            DepenetrationInfo = info;
            m_DepenTarget = Vector3.ClampMagnitude(push, 0.06f);
        }

        /// <summary>Distance a point inside the collider must travel along dir (unit) to leave it; 0 when outside.</summary>
        public static float ExitDistance(Collider c, Vector3 p, Vector3 dir)
        {
            if (c is BoxCollider box)
            {
                var t = box.transform; var l = t.InverseTransformPoint(p) - box.center; var h = box.size * 0.5f;
                if (Mathf.Abs(l.x) >= h.x || Mathf.Abs(l.y) >= h.y || Mathf.Abs(l.z) >= h.z) return 0f;
                var dl = t.InverseTransformDirection(dir); // local direction (scaled)
                float best = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    float di = dl[i]; if (Mathf.Abs(di) < 1e-6f) continue;
                    float face = di > 0f ? h[i] : -h[i];
                    float tt = (face - l[i]) / di; if (tt >= 0f && tt < best) best = tt;
                }
                return best == float.MaxValue ? 0f : best; // t is in world units because dir is a unit world vector
            }
            if (c is CapsuleCollider cap)
            {
                var t = cap.transform; float sc = Mathf.Max(1e-5f, t.lossyScale.x);
                var axis = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
                float half = Mathf.Max(0f, cap.height * 0.5f - cap.radius);
                var a = t.TransformPoint(cap.center + axis * half); var b = t.TransformPoint(cap.center - axis * half);
                var ab = b - a; float u = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                var off = p - (a + ab * u); float r = cap.radius * sc; float d2 = off.sqrMagnitude;
                if (d2 >= r * r) return 0f;
                float od = Vector3.Dot(off, dir);
                return -od + Mathf.Sqrt(Mathf.Max(0f, r * r - (d2 - od * od))); // leave the sphere around the closest point
            }
            float sd = SignedDistance(c, p);
            return sd < 0f ? -sd : 0f;
        }

        /// <summary>Depth of a point inside a collider (0 outside) and the outward normal of the nearest face.</summary>
        /// <summary>
        /// A contact volume reduced to plain numbers (world centre, inverse rotation, half extents / segment and radius) so
        /// per-vertex queries do not touch the Transform API. Rebuilt once per solve.
        /// </summary>
        public struct ContactShape
        {
            public int kind; // 0 box, 1 capsule, 2 sphere, 3 other
            public Collider collider;
            public Vector3 center; public Quaternion rot, invRot; public Vector3 half; public float scale;
            public Vector3 a, b; public float radius;

            public static ContactShape From(Collider c)
            {
                var sh = new ContactShape { collider = c, kind = 3, scale = 1f, rot = Quaternion.identity, invRot = Quaternion.identity };
                if (c == null) return sh;
                var t = c.transform; var ls = t.lossyScale; float sc = Mathf.Max(1e-5f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
                if (c is BoxCollider box) { sh.kind = 0; sh.center = t.TransformPoint(box.center); sh.rot = t.rotation; sh.invRot = Quaternion.Inverse(t.rotation); sh.half = box.size * 0.5f; sh.scale = sc; }
                else if (c is CapsuleCollider cap)
                {
                    sh.kind = 1; var axis = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
                    float h = Mathf.Max(0f, cap.height * 0.5f - cap.radius);
                    sh.a = t.TransformPoint(cap.center + axis * h); sh.b = t.TransformPoint(cap.center - axis * h); sh.radius = cap.radius * Mathf.Abs(ls.x);
                }
                else if (c is SphereCollider sph) { sh.kind = 2; sh.a = t.TransformPoint(sph.center); sh.radius = sph.radius * sc; }
                return sh;
            }
        }

        /// <summary>Signed distance from a point to the shape's surface (negative inside).</summary>
        public static float SignedDistance(in ContactShape s, Vector3 p)
        {
            switch (s.kind)
            {
                case 0:
                {
                    var l = s.invRot * (p - s.center) / s.scale;
                    var q = new Vector3(Mathf.Abs(l.x) - s.half.x, Mathf.Abs(l.y) - s.half.y, Mathf.Abs(l.z) - s.half.z);
                    float outside = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f)).magnitude;
                    float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
                    return (outside + inside) * s.scale;
                }
                case 1:
                {
                    var ab = s.b - s.a; float tt = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / ab.sqrMagnitude) : 0f;
                    return Vector3.Distance(p, s.a + ab * tt) - s.radius;
                }
                case 2: return Vector3.Distance(p, s.a) - s.radius;
                default: return s.collider != null ? Vector3.Distance(p, s.collider.ClosestPoint(p)) : float.MaxValue;
            }
        }

        /// <summary>Distance a point inside the shape must travel along dir (unit, world) to leave it; 0 when outside.</summary>
        public static float ExitDistance(in ContactShape s, Vector3 p, Vector3 dir)
        {
            if (s.kind == 0)
            {
                var l = s.invRot * (p - s.center) / s.scale;
                if (Mathf.Abs(l.x) >= s.half.x || Mathf.Abs(l.y) >= s.half.y || Mathf.Abs(l.z) >= s.half.z) return 0f;
                var dl = s.invRot * dir; float best = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    float di = dl[i]; if (Mathf.Abs(di) < 1e-6f) continue;
                    float tt = ((di > 0f ? s.half[i] : -s.half[i]) - l[i]) / di;
                    if (tt >= 0f && tt < best) best = tt;
                }
                return best == float.MaxValue ? 0f : best * s.scale;
            }
            if (s.kind == 1 || s.kind == 2)
            {
                var ab = s.kind == 1 ? s.b - s.a : Vector3.zero; float tt = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / ab.sqrMagnitude) : 0f;
                var off = p - (s.a + ab * tt); float d2 = off.sqrMagnitude; float r = s.radius;
                if (d2 >= r * r) return 0f;
                float od = Vector3.Dot(off, dir);
                return -od + Mathf.Sqrt(Mathf.Max(0f, r * r - (d2 - od * od)));
            }
            float sd = SignedDistance(s, p); return sd < 0f ? -sd : 0f;
        }

        /// <summary>Depth of a point inside the shape (0 outside) and the outward normal of the nearest face.</summary>
        public static float DepthAndNormal(in ContactShape s, Vector3 p, out Vector3 normal)
        {
            normal = Vector3.up;
            switch (s.kind)
            {
                case 0:
                {
                    var l = s.invRot * (p - s.center) / s.scale;
                    var q = new Vector3(s.half.x - Mathf.Abs(l.x), s.half.y - Mathf.Abs(l.y), s.half.z - Mathf.Abs(l.z));
                    if (q.x <= 0f || q.y <= 0f || q.z <= 0f) return 0f;
                    if (q.x <= q.y && q.x <= q.z) { normal = s.rot * (Vector3.right * Mathf.Sign(l.x)); return q.x * s.scale; }
                    if (q.y <= q.z) { normal = s.rot * (Vector3.up * Mathf.Sign(l.y)); return q.y * s.scale; }
                    normal = s.rot * (Vector3.forward * Mathf.Sign(l.z)); return q.z * s.scale;
                }
                case 1:
                case 2:
                {
                    var ab = s.kind == 1 ? s.b - s.a : Vector3.zero; float tt = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - s.a, ab) / ab.sqrMagnitude) : 0f;
                    var off = p - (s.a + ab * tt); float dist = off.magnitude; float depth = s.radius - dist;
                    if (depth <= 0f) return 0f;
                    normal = dist > 1e-5f ? off / dist : Vector3.up; return depth;
                }
                default:
                {
                    float sd = SignedDistance(s, p); if (sd >= 0f) return 0f;
                    var cp = s.collider.ClosestPoint(p); normal = (p - cp).sqrMagnitude > 1e-10f ? (p - cp).normalized : Vector3.up; return -sd;
                }
            }
        }

        public static float DepthAndNormal(Collider c, Vector3 p, out Vector3 normal) => DepthAndNormal(ContactShape.From(c), p, out normal);

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
            for (int f = 0; f < 5; f++) ApplyFingerCurl(f, m_Curl[f], m_Spread, m_ThumbOpposition, m_TipExtra[f]);
        }

        /// <summary>Degrees the end joints close beyond the common curl at tipExtra = 1 (intermediate, distal).</summary>
        const float kTipExtraIntermediate = 55f, kTipExtraDistal = 50f;

        void ApplyFingerCurl(int f, float curl, float spread, float opposition = 0f, float tipExtra = 0f)
        {
            var c = m_Fingers[f]; if (c == null) return;
            Transform[] joints = { c.metacarpal, c.proximal, c.intermediate, c.distal };
            float[] max = f == 0
                ? new[] { m_ThumbMaxDegrees.x, m_ThumbMaxDegrees.y, m_ThumbMaxDegrees.y, m_ThumbMaxDegrees.z } // thumb rigs may or may not have an intermediate joint
                : new[] { 0f, m_ProximalMaxDegrees, m_IntermediateMaxDegrees, m_DistalMaxDegrees };
            float[] extra = f == 0 ? new[] { 0f, 0f, 0f, kTipExtraDistal * 0.6f } : new[] { 0f, 0f, kTipExtraIntermediate, kTipExtraDistal };
            for (int j = 0; j < 4; j++)
            {
                var t = joints[j]; if (t == null || max[j] <= 0f) continue;
                var rot = c.rest[j] * Quaternion.AngleAxis(Mathf.Min(curl * max[j] + tipExtra * extra[j], max[j] + extra[j]), c.curlAxisLocal[j]);
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
                if (m_SolvedAgainst != grip || m_Targets.Count == 0 || (m_OtherHand == null) != (m_HeldWeapon.HandCount < 2)) RefreshTargets();
                m_SolvedAgainst = grip;
                RefreshOwnShapes();
                RefreshShapes();
                SolvePalmDepenetration();
                bool indexFree = m_Anchor.pose == null || m_Anchor.pose.indexFollowsTrigger;
                bool shootingHand = m_Anchor.role == GripRole.Primary;
                for (int f = 0; f < 5; f++)
                {
                    m_ContactTipExtra[f] = 0f;
                    if (f == 1 && indexFree) { m_ContactCurl[f] = -1f; continue; }
                    if (m_Fingers[f] == null) { m_ContactCurl[f] = -1f; continue; }
                    m_ContactCurl[f] = f == 0 && shootingHand ? SolveThumbOnPalmSide(m_TargetCurl[f], grip) : SolveFinger(f, m_TargetCurl[f], grip);
                    // after the first contact the end joints keep closing until the fingertip itself touches
                    // (the shooting-hand thumb already optimises its tip distance in the opposition/curl search; its end joint bends away from the frame)
                    if (m_ContactCurl[f] >= 0f && !(f == 0 && shootingHand)) m_ContactTipExtra[f] = SolveTipExtra(f, m_ContactCurl[f], 0f, grip);
                    // a finger whose tip still does not touch (no contact at any curl, or a first contact on the proximal segment that
                    // leaves the tip hanging) closes to the pose that brings its tip nearest to the surface
                    if (f >= 2 && !TipTouches(f, m_ContactCurl[f] >= 0f ? m_ContactCurl[f] : m_TargetCurl[f], m_ContactTipExtra[f], grip))
                    {
                        SolveNearestApproach(f, m_TargetCurl[f], grip, out float nc, out float ne);
                        if (nc >= 0f) { m_ContactCurl[f] = nc; m_ContactTipExtra[f] = ne; }
                    }
                }
                if (!shootingHand) m_ContactThumbOpposition = 0f;
                // the solve posed the joints; ApplyCurls below restores the smoothed state
            }
            for (int f = 0; f < 5; f++) { if (m_ContactCurl[f] >= 0f) m_TargetCurl[f] = m_ContactCurl[f]; m_TargetTipExtra[f] = m_ContactTipExtra[f]; }
            m_TargetThumbOpposition = m_ContactThumbOpposition;
        }

        /// <summary>
        /// With the common curl fixed at its contact value, closes the end joints (intermediate + distal) until the distal
        /// segment or the tip touches a contact surface, never pushing any sample inside. Returns the extra amount (0..1).
        /// </summary>
        float m_DeepMargin = 0.001f;
        float SolveTipExtra(int f, float curl, float opposition, Collider grip)
        {
            var c = m_Fingers[f]; if (c == null || c.tip == null || c.distal == null) return 0f;
            float spread = m_TargetSpread;
            m_DeepMargin = 0.002f;
            try { return SolveTipExtraInner(f, curl, opposition, grip, c, spread); }
            finally { m_DeepMargin = 0.001f; }
        }

        float SolveTipExtraInner(int f, float curl, float opposition, Collider grip, JointChain c, float spread)
        {
            // already touching with the tip? then nothing to add
            ApplyFingerCurl(f, curl, spread, opposition, 0f);
            if (TipState(c, grip) >= 1) return 0f;
            float lo = 0f;
            for (float e = 0.1f; e <= 1f + 1e-4f; e += 0.1f)
            {
                ApplyFingerCurl(f, curl, spread, opposition, e);
                int whole = ContactState(f, grip);
                if (whole == 2) { // bisect back to the last pose that does not penetrate
                    float hi = e;
                    for (int i = 0; i < 4; i++) { float mid = 0.5f * (lo + hi); ApplyFingerCurl(f, curl, spread, opposition, mid); if (ContactState(f, grip) == 2) hi = mid; else lo = mid; }
                    return lo;
                }
                if (TipState(c, grip) >= 1) return e;
                lo = e;
            }
            return lo;
        }

        int TipState(JointChain c, Collider grip) => Sample(c.tip.position, grip); // the fingertip itself must touch

        bool TipTouches(int f, float curl, float tipExtra, Collider grip)
        {
            var c = m_Fingers[f]; if (c == null || c.tip == null) return true;
            ApplyFingerCurl(f, curl, m_TargetSpread, 0f, tipExtra);
            float d = float.MaxValue;
            if (m_Shapes.Count == 0) d = SignedDistance(grip, c.tip.position); else for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], c.tip.position); if (sd < d) d = sd; }
            return d < m_FingerRadius + 0.002f;
        }

        /// <summary>
        /// For a finger that never touches: the (curl, tipExtra) pose, searched from the pose curl to a full curl, whose tip
        /// bone comes nearest to a contact surface without any sample going too deep. Leaves curl = -1 when nothing beats the
        /// pose curl by more than 1 mm.
        /// </summary>
        void SolveNearestApproach(int f, float minCurl, Collider grip, out float curl, out float tipExtra)
        {
            curl = -1f; tipExtra = 0f;
            var c = m_Fingers[f]; if (c == null || c.tip == null) return;
            float spread = m_TargetSpread;
            float Gap() { float d = float.MaxValue; if (m_Shapes.Count == 0) return SignedDistance(grip, c.tip.position); for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], c.tip.position); if (sd < d) d = sd; } return d; }
            ApplyFingerCurl(f, m_ContactCurl[f] >= 0f ? m_ContactCurl[f] : minCurl, spread, 0f, m_ContactTipExtra[f]);
            float best = Gap() - 0.0005f, bestCurl = -1f, bestExtra = 0f;
            // from half a curl more open than the pose (a short grip may be reached by a straighter finger) to fully closed
            for (float cu = Mathf.Max(0f, minCurl - 0.5f); cu <= 1f + 1e-4f; cu += 0.05f)
                for (float e = 0f; e <= 1f + 1e-4f; e += 0.25f)
                {
                    ApplyFingerCurl(f, cu, spread, 0f, e);
                    if (ContactState(f, grip) == 2) break; // deeper end joints only go further in
                    float g = Gap();
                    if (g < best) { best = g; bestCurl = cu; bestExtra = e; }
                }
            if (bestCurl >= 0f) { curl = bestCurl; tipExtra = bestExtra; }
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
                // contact somewhere between lo (free) and cc: bisect to the curl where the flesh just touches the surface
                float hi = cc; float touching = s == 1 ? cc : float.NaN;
                for (int i = 0; i < 5; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    int sm = Contact(f, mid, spread, grip);
                    if (sm == 0) lo = mid; else { hi = mid; if (sm == 1) touching = mid; }
                }
                return float.IsNaN(touching) ? lo : touching;
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
            float TipGap() { float d = float.MaxValue; for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], c.tip.position); if (sd < d) d = sd; } return d == float.MaxValue ? SignedDistance(grip, c.tip.position) : d; }
            // Joint search over opposition (swing out of the palm) and curl (close): the thumb must stay on the palm's side
            // of the grip and outside every contact volume; among those poses take the one whose tip is closest to the
            // frame surface, so the thumb rests ON the frame instead of hovering above it.
            float bestScore = float.MaxValue, bestOpp = 0f, bestCurl = minCurl; bool found = false;
            float maxCurl = 1f, lowCurl = Mathf.Max(0f, minCurl - 0.3f);
            for (float o = -60f; o <= 75f + 1e-3f; o += 7.5f)
            for (float curl = lowCurl; curl <= maxCurl + 1e-4f; curl += 0.1f)
            {
                ApplyFingerCurl(0, curl, spread, o);
                if (TipSide() <= 0.003f) continue;
                int state = ContactState(0, grip);
                if (state == 2) continue;
                float gap = TipGap();
                // prefer touching (gap ~ flesh radius), then the least opposition (closest to the natural thumb)
                float score = Mathf.Abs(gap - m_FingerRadius) + Mathf.Abs(o) * 0.0001f;
                if (score < bestScore) { bestScore = score; bestOpp = o; bestCurl = curl; found = true; }
            }
            if (found)
            {
                // refine around the best grid cell (opposition ±5 deg, curl ±0.07) so the thumb lands on the frame, not near it
                float o0 = bestOpp, c0 = bestCurl;
                for (int pass = 0; pass < 2; pass++)
                {
                    float oStep = pass == 0 ? 2.5f : 1f, cStep = pass == 0 ? 0.025f : 0.01f, oRange = pass == 0 ? 5f : 2f, cRange = pass == 0 ? 0.07f : 0.03f;
                    o0 = bestOpp; c0 = bestCurl;
                    for (float o = o0 - oRange; o <= o0 + oRange + 1e-3f; o += oStep)
                    for (float curl = Mathf.Max(0f, c0 - cRange); curl <= Mathf.Min(1f, c0 + cRange) + 1e-4f; curl += cStep)
                    {
                        ApplyFingerCurl(0, curl, spread, o);
                        if (TipSide() <= 0.003f) continue;
                        int state = ContactState(0, grip);
                        if (state == 2) continue;
                        float gap = TipGap();
                        float score = Mathf.Abs(gap - m_FingerRadius) + Mathf.Abs(o) * 0.0001f;
                        if (score < bestScore) { bestScore = score; bestOpp = o; bestCurl = curl; }
                    }
                }
            }
            if (m_DebugThumb)
            {
                int sideFail = 0, deepFail = 0, ok = 0; float minGapSeen = float.MaxValue; float oppAtMin = 0f, curlAtMin = 0f;
                for (float o = -60f; o <= 75f + 1e-3f; o += 15f)
                for (float curl = lowCurl; curl <= maxCurl + 1e-4f; curl += 0.2f)
                {
                    ApplyFingerCurl(0, curl, spread, o);
                    if (TipSide() <= 0.003f) { sideFail++; continue; }
                    if (ContactState(0, grip) == 2) { deepFail++; continue; }
                    ok++; float g = TipGap(); if (g < minGapSeen) { minGapSeen = g; oppAtMin = o; curlAtMin = curl; }
                }
                ApplyFingerCurl(0, bestCurl, spread, bestOpp);
                var tl = grip.transform.InverseTransformPoint(c.tip.position) * 1000f;
                Debug.Log($"[VRRange] thumb solve {(m_IsLeft ? "L" : "R")} on {grip.name}: opposition {bestOpp:0}, curl {bestCurl:0.00}, score {bestScore * 1000f:0.0} mm, found {found}; tip grip-local ({tl.x:0},{tl.y:0},{tl.z:0}) mm; coarse grid: {ok} valid, {sideFail} crossed, {deepFail} inside; smallest valid gap {minGapSeen * 1000f:0.0} mm at opp {oppAtMin:0} curl {curlAtMin:0.0}; targets {m_Shapes.Count}");
            }
            if (!found) { m_ContactThumbOpposition = 0f; return minCurl; }
            m_ContactThumbOpposition = bestOpp;
            return bestCurl;
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
            // the thumb's metacarpal segment (thenar) moves with the opposition, so it is sampled too; its flesh compresses
            // against a grip, so it only counts as "too deep" when the bone itself is within 4 mm of the surface
            Transform[] pts = f == 0 && c.metacarpal != null ? new[] { c.metacarpal, c.proximal, c.intermediate, c.distal, c.tip } : new[] { c.proximal, c.intermediate, c.distal, c.tip };
            bool thenar = f == 0 && c.metacarpal != null;
            Vector3 prev = default; bool hasPrev = false;
            for (int i = 0; i < pts.Length; i++)
            {
                var t = pts[i]; if (t == null) continue;
                var p = t.position;
                bool thenarSeg = thenar && i == 1; // metacarpal -> proximal
                if (hasPrev)
                {
                    for (float u = 0.3f; u < 0.95f; u += 0.3f) { int s = thenarSeg ? SampleDeep(Vector3.Lerp(prev, p, u), grip, 0.002f) : Sample(Vector3.Lerp(prev, p, u), grip); if (s > result) result = s; }
                }
                // the thumb's base joint sits in the thenar, which is allowed to compress against the grip: bone clearance only
                if (i > 0) { int s = thenar && i == 1 ? SampleDeep(p, grip, 0.002f) : Sample(p, grip); if (s > result) result = s; }
                prev = p; hasPrev = true;
                if (result == 2) return 2;
            }
            return result;
        }

        /// <summary>Like Sample, but "too deep" only when the bone is within boneClearance of the surface (compressible flesh).</summary>
        int SampleDeep(Vector3 p, Collider grip, float boneClearance)
        {
            float d = float.MaxValue;
            if (m_Shapes.Count == 0) d = SignedDistance(grip, p);
            else for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], p); if (sd < d) d = sd; }
            if (d < boneClearance) return 2;
            return d < m_FingerRadius + 0.001f ? 1 : 0;
        }

        int Sample(Vector3 p, Collider grip)
        {
            // smallest signed distance to any contact target (the grip collider alone if no targets are known)
            float d = float.MaxValue;
            if (m_Shapes.Count == 0) d = SignedDistance(grip, p);
            else for (int i = 0; i < m_Shapes.Count; i++) { float sd = SignedDistance(m_Shapes[i], p); if (sd < d) d = sd; }
            // the bone line sits one flesh radius outside a touching surface: deeper than 1 mm of flesh overlap is "too deep"
            // (2 mm while the end joints close onto the surface: the finger pad flattens a little)
            if (d < m_FingerRadius - m_DeepMargin) return 2;
            return d < m_FingerRadius + 0.001f ? 1 : 0;
        }

        /// <summary>Signed distance from a point to a collider's surface (negative inside). Exact for boxes, capsules and spheres; ClosestPoint otherwise (never negative).</summary>
        public static float SignedDistance(Collider c, Vector3 p) => SignedDistance(ContactShape.From(c), p);

        /// <summary>World positions of a finger's joints (metacarpal, proximal, intermediate, distal, tip; null entries become NaN) for tests / debugging.</summary>
        public bool TryGetJoints(Finger f, out Vector3[] world)
        {
            var c = m_Fingers[(int)f];
            if (c == null) { world = null; return false; }
            Transform[] j = { c.metacarpal, c.proximal, c.intermediate, c.distal, c.tip };
            world = new Vector3[5];
            for (int i = 0; i < 5; i++) world[i] = j[i] != null ? j[i].position : new Vector3(float.NaN, float.NaN, float.NaN);
            return true;
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
