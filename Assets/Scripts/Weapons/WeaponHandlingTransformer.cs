using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;
using VRRange.Core;

namespace VRRange.Weapons
{
    /// <summary>
    /// XRI grab transformer that gives each weapon a believable sense of weight: first-order lag (inertia) behind the
    /// hand, weight-induced muzzle sag, natural sway, two-point aiming when the support hand grabs a handguard, smooth
    /// grip transitions, a smooth hand-over when the shooting hand lets go of a supported weapon, and grip-dependent
    /// recoil that accumulates during rapid fire. Everything is applied to the weapon's target pose only; the tracked
    /// controllers and the camera are never touched, and lag is clamped so aiming stays possible.
    /// It runs for every interactor that selects the XRGrabInteractable (the rig's NearFarInteractor, direct
    /// interactors in tests): XRI hands the selecting interactors' attach transforms to <see cref="Process"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponHandlingTransformer : XRBaseGrabTransformer, IXRDropTransformer
    {
        /// <summary>No pose processing on drop; we only need the callback to clear grip state.</summary>
        public bool canProcessOnDrop => false;

        [SerializeField] WeaponDefinition m_Definition;
        [SerializeField] Transform m_PrimaryGrip;   // weapon-local primary grip (the XRGrabInteractable attach transform)
        [SerializeField] Transform m_SupportGrip;   // weapon-local support grip (secondary attach), optional
        [SerializeField] bool m_WeightSimulation = true;
        [Tooltip("Write one '[VRRange][Handling]' line per second to the log while held (hands, sag, lag, recoil). Cheap; useful in Player.log on the headset.")]
        [SerializeField] bool m_LogWhileHeld = true;

        protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple;

        // grip state
        IXRSelectInteractor m_Primary, m_Support;
        Pose m_PrimaryGripLocal, m_SupportGripLocal;
        float m_TwoHandBlend;          // 0 = one hand, 1 = two hands
        float m_TwoHandTarget;
        bool m_HasState;
        // hand-over (previous support hand became the primary hand): offset blended out so the weapon glides into the new grip
        bool m_HandoverPending; float m_HandoverWeight; Vector3 m_HandoverDeltaPos; Quaternion m_HandoverDeltaRot = Quaternion.identity;
        // lag state
        Vector3 m_LagPos; Quaternion m_LagRot = Quaternion.identity;
        // recoil state (weapon-local): position back along -Z, pitch up, yaw
        Vector3 m_RecoilPos; Vector3 m_RecoilEuler;
        float m_Instability;
        float m_SwaySeed;
        float m_LastDynamicTime = -1f;
        float m_LastShotTime = -10f;
        Vector3 m_LastHandPos; float m_HandSpeed;
        float m_NextLogTime;
        // last computed values (read by tests / the log)
        float m_CurrentSag, m_CurrentLagMetres;

        public WeaponDefinition Definition { get => m_Definition; set => m_Definition = value; }
        public bool IsTwoHanded => m_Support != null;
        public float TwoHandBlend => m_TwoHandBlend;
        public float Instability => m_Instability;
        public float HandSpeed => m_HandSpeed;
        public bool WeightSimulation { get => m_WeightSimulation; set => m_WeightSimulation = value; }
        public Transform PrimaryGrip => m_PrimaryGrip;
        /// <summary>World position of the primary palm centroid as actually used for the current shooting hand (mirrored in X for a left hand; see <see cref="RefreshInteractors"/>).</summary>
        public Vector3 PrimaryGripWorld => m_Grab != null ? m_Grab.transform.TransformPoint(m_PrimaryGripLocal.position) : m_PrimaryGrip != null ? m_PrimaryGrip.position : transform.position;
        XRGrabInteractable m_Grab;
        public Transform SupportGrip => m_SupportGrip;
        /// <summary>Accumulated recoil pitch in degrees (positive = muzzle up).</summary>
        public float RecoilPitch => -m_RecoilEuler.x;
        public float RecoilYaw => m_RecoilEuler.y;
        /// <summary>Weight sag applied this frame (degrees, muzzle down).</summary>
        public float CurrentSagDegrees => m_CurrentSag;
        /// <summary>Distance the grip trails the hand this frame (metres).</summary>
        public float CurrentLagMetres => m_CurrentLagMetres;
        public IXRSelectInteractor PrimaryInteractor => m_Primary;
        public IXRSelectInteractor SupportInteractor => m_Support;
        public bool IsHandingOver => m_HandoverWeight > 0.01f;

        public void Configure(WeaponDefinition definition, Transform primaryGrip, Transform supportGrip)
        {
            m_Definition = definition; m_PrimaryGrip = primaryGrip; m_SupportGrip = supportGrip;
        }

        void Awake() { m_SwaySeed = Random.Range(0f, 100f); }

        /// <summary>Called by <see cref="Weapon"/> on each shot. Values are the definition's base kick; grip and mass scaling happen here.</summary>
        public void AddRecoilImpulse(float kickBackMetres, float kickUpDegrees, float kickSideDegrees)
        {
            if (m_Definition == null) return;
            float scale = WeaponHandlingMath.RecoilScale(m_Definition.recoilUnsupportedMultiplier, m_Definition.recoilSupportedMultiplier, m_TwoHandBlend, m_Definition.virtualMassKg);
            // instability (built up by rapid fire) makes every further shot kick and wander more: the muzzle climbs and walks
            float instab = 1f + 0.6f * m_Instability * (1f - 0.7f * m_TwoHandBlend);
            m_RecoilPos += new Vector3(0f, kickBackMetres * 0.1f, -kickBackMetres) * scale;
            float yaw = Random.Range(-kickSideDegrees, kickSideDegrees) * scale * instab;
            m_RecoilEuler += new Vector3(-kickUpDegrees * scale * instab, yaw, Random.Range(-kickSideDegrees, kickSideDegrees) * 0.4f * scale);
            ClampRecoil();
            m_Instability = Mathf.Clamp01(m_Instability + m_Definition.instabilityPerShot * Mathf.Lerp(1f, 0.4f, m_TwoHandBlend));
            m_LastShotTime = Time.time;
        }

        void ClampRecoil()
        {
            if (m_Definition == null) return;
            m_RecoilEuler.x = Mathf.Clamp(m_RecoilEuler.x, -m_Definition.maxRecoilPitchDegrees, 2f);
            m_RecoilEuler.y = Mathf.Clamp(m_RecoilEuler.y, -m_Definition.maxRecoilYawDegrees, m_Definition.maxRecoilYawDegrees);
            m_RecoilEuler.z = Mathf.Clamp(m_RecoilEuler.z, -10f, 10f);
            m_RecoilPos = Vector3.ClampMagnitude(m_RecoilPos, 0.12f);
        }

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            base.OnGrab(grabInteractable);
            RefreshInteractors(grabInteractable);
            m_HasState = false;
            m_RecoilPos = Vector3.zero; m_RecoilEuler = Vector3.zero;
            m_HandoverPending = false; m_HandoverWeight = 0f;
            m_NextLogTime = Time.time + 1f;
        }

        public override void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose, Vector3 localScale)
        {
            base.OnGrabCountChanged(grabInteractable, targetPose, localScale);
            var oldPrimary = m_Primary; var oldSupport = m_Support;
            RefreshInteractors(grabInteractable);
            // The shooting hand let go while the support hand kept hold: the support hand becomes the shooting hand.
            // The weapon must not drop or snap, so the pose difference is blended out over the grip transition time.
            if (oldSupport != null && m_Primary == oldSupport && oldPrimary != null && oldPrimary != m_Primary && m_HasState)
            {
                m_HandoverPending = true;
                Debug.Log($"[VRRange][Handling] {name}: hand-over to {m_Primary.transform.name} (previous shooting hand released)");
            }
        }

        void RefreshInteractors(XRGrabInteractable grab)
        {
            m_Grab = grab;
            m_Primary = null; m_Support = null;
            var list = grab.interactorsSelecting;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is XRSocketInteractor) continue;
                if (m_Primary == null) m_Primary = list[i]; else if (m_Support == null) m_Support = list[i];
            }
            var t = grab.transform;
            var prim = m_PrimaryGrip != null ? m_PrimaryGrip : grab.attachTransform != null ? grab.attachTransform : t;
            m_PrimaryGripLocal = new Pose(t.InverseTransformPoint(prim.position), Quaternion.Inverse(t.rotation) * prim.rotation);
            var sup = m_SupportGrip != null ? m_SupportGrip : grab.secondaryAttachTransform;
            m_SupportGripLocal = sup != null ? new Pose(t.InverseTransformPoint(sup.position), Quaternion.Inverse(t.rotation) * sup.rotation) : m_PrimaryGripLocal;
            // The grip points are the palm centroids, authored for a right shooting hand on the weapon's +X side. A left
            // shooting hand holds the mirror image (its palm on -X, the support hand on +X).
            if (m_Primary is XRBaseInputInteractor input && input.handedness == InteractorHandedness.Left)
            {
                m_PrimaryGripLocal.position = new Vector3(-m_PrimaryGripLocal.position.x, m_PrimaryGripLocal.position.y, m_PrimaryGripLocal.position.z);
                m_SupportGripLocal.position = new Vector3(-m_SupportGripLocal.position.x, m_SupportGripLocal.position.y, m_SupportGripLocal.position.z);
            }
            m_TwoHandTarget = m_Support != null && sup != null ? 1f : 0f;
        }

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            if (m_Primary == null) return; // socketed or not held by a hand: leave the default/socket pose untouched
            bool integrate = updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic;
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic && updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender) return;

            var handT = m_Primary.GetAttachTransform(grabInteractable);
            if (handT == null) return;
            var hand = new Pose(handT.position, handT.rotation);
            float dt = integrate ? ComputeDeltaTime() : 0f;

            // ---- ideal pose: one-handed, then two-point aim ----
            var ideal = WeaponHandlingMath.PoseFromGrip(hand, m_PrimaryGripLocal);
            var def = m_Definition;
            float transition = def != null ? Mathf.Max(0.02f, def.gripTransitionSeconds) : 0.2f;
            if (integrate) m_TwoHandBlend = Mathf.MoveTowards(m_TwoHandBlend, m_TwoHandTarget, dt / transition);
            bool twoPoint = def != null && def.supportHandInfluence > 0f && (m_SupportGripLocal.position - m_PrimaryGripLocal.position).sqrMagnitude > 0.01f * 0.01f;
            if (m_Support != null && twoPoint)
            {
                var supT = m_Support.GetAttachTransform(grabInteractable);
                if (supT != null)
                {
                    var gripToSupportLocal = m_SupportGripLocal.position - m_PrimaryGripLocal.position;
                    var twoHandRot = WeaponHandlingMath.TwoPointAim(ideal.rotation, hand.position, supT.position, gripToSupportLocal, def.supportHandInfluence);
                    var rot = Quaternion.Slerp(ideal.rotation, twoHandRot, m_TwoHandBlend);
                    ideal = new Pose(hand.position - rot * m_PrimaryGripLocal.position, rot);
                }
            }
            else if (m_Support == null && m_TwoHandBlend > 0f && twoPoint)
            {
                // support hand released: blend back from the last two-handed rotation toward one-handed
                var rot = Quaternion.Slerp(ideal.rotation, m_LagRot, m_TwoHandBlend * 0.5f);
                ideal = new Pose(hand.position - rot * m_PrimaryGripLocal.position, rot);
            }

            // ---- hand-over: start from where the weapon is and glide into the new hand's grip ----
            if (m_HandoverPending)
            {
                m_HandoverPending = false;
                m_HandoverDeltaPos = m_LagPos - ideal.position;
                m_HandoverDeltaRot = Quaternion.Inverse(ideal.rotation) * m_LagRot;
                m_HandoverWeight = m_HandoverDeltaPos.magnitude < 0.3f && Quaternion.Angle(Quaternion.identity, m_HandoverDeltaRot) < 90f ? 1f : 0f;
            }
            if (m_HandoverWeight > 0f)
            {
                if (integrate) m_HandoverWeight = Mathf.MoveTowards(m_HandoverWeight, 0f, dt / (transition * 1.5f));
                float w = Mathf.SmoothStep(0f, 1f, m_HandoverWeight);
                ideal = new Pose(ideal.position + m_HandoverDeltaPos * w, ideal.rotation * Quaternion.Slerp(Quaternion.identity, m_HandoverDeltaRot, w));
            }

            if (!m_WeightSimulation || def == null)
            {
                targetPose = ideal; m_LagPos = ideal.position; m_LagRot = ideal.rotation; m_HasState = true;
                return;
            }

            // ---- inertia (first-order lag, clamped) ----
            if (!m_HasState) { m_LagPos = ideal.position; m_LagRot = ideal.rotation; m_LastHandPos = hand.position; m_HasState = true; }
            if (integrate)
            {
                m_HandSpeed = dt > 0f ? Mathf.Lerp(m_HandSpeed, (hand.position - m_LastHandPos).magnitude / dt, 0.3f) : m_HandSpeed;
                m_LastHandPos = hand.position;
                float response = Mathf.Lerp(def.oneHandResponseSeconds, def.twoHandResponseSeconds, m_TwoHandBlend) * (1f + 0.5f * m_Instability);
                float k = WeaponHandlingMath.LagFactor(dt, response);
                m_LagPos = Vector3.Lerp(m_LagPos, ideal.position, k);
                m_LagRot = Quaternion.Slerp(m_LagRot, ideal.rotation, k);
                m_Instability = Mathf.MoveTowards(m_Instability, 0f, def.instabilityDecayPerSecond * dt * 0.4f);
                // recoil spring recovery: slow while shots keep coming (climb accumulates), full speed once the trigger rests
                float rec = WeaponHandlingMath.RecoverySpeed(def.recoveryUnsupported, def.recoverySupported, m_TwoHandBlend);
                if (Time.time - m_LastShotTime < 0.25f) rec *= 0.6f;
                float rk = 1f - Mathf.Exp(-rec * dt);
                m_RecoilPos = Vector3.Lerp(m_RecoilPos, Vector3.zero, rk);
                m_RecoilEuler = Vector3.Lerp(m_RecoilEuler, Vector3.zero, rk);
            }
            var pos = WeaponHandlingMath.ClampLag(ideal.position, m_LagPos, def.maxPositionLagMetres);
            var rotLag = WeaponHandlingMath.ClampLag(ideal.rotation, m_LagRot, def.maxRotationLagDegrees);
            if (integrate) { m_LagPos = pos; m_LagRot = rotLag; }

            // ---- weight sag + sway + recoil, all about the grip point ----
            // Sag is the authored one-/two-handed value; a fast hand move droops the muzzle a little more (front-heavy weapons more).
            float dynamicSag = Mathf.Clamp(m_HandSpeed * def.balanceForwardMetres * 8f, 0f, 6f) * (1f - 0.8f * m_TwoHandBlend);
            float sag = WeaponHandlingMath.Sag(def.oneHandSagDegrees, def.twoHandSagDegrees, m_TwoHandBlend) + dynamicSag;
            m_CurrentSag = sag;
            var sway = WeaponHandlingMath.Sway(Time.time, def.swayFrequencyHz, WeaponHandlingMath.SwayAmplitude(def.oneHandSwayDegrees, def.twoHandSwayDegrees, m_TwoHandBlend, m_Instability), m_SwaySeed);
            var localOffset = Quaternion.Euler(sag + sway.x + m_RecoilEuler.x, sway.y + m_RecoilEuler.y, m_RecoilEuler.z);
            var finalRot = rotLag * localOffset;
            // keep the grip point on the (lagged) hand position; recoil shifts it back along the barrel
            var gripWorld = pos + rotLag * m_PrimaryGripLocal.position; // where the grip currently is
            var finalPos = gripWorld - finalRot * m_PrimaryGripLocal.position + finalRot * m_RecoilPos;
            m_CurrentLagMetres = Vector3.Distance(gripWorld, hand.position);
            targetPose = new Pose(finalPos, finalRot);

            if (integrate && m_LogWhileHeld && Time.time >= m_NextLogTime)
            {
                m_NextLogTime = Time.time + 1f;
                Debug.Log($"[VRRange][Handling] {name}: hands={(m_Support != null ? 2 : 1)} via {m_Primary.transform.name} ({m_Primary.GetType().Name}) blend={m_TwoHandBlend:0.00} sag={sag:0.0}deg lag={m_CurrentLagMetres * 100f:0.0}cm recoil pitch={RecoilPitch:0.0}deg yaw={RecoilYaw:0.0}deg instability={m_Instability:0.00} handSpeed={m_HandSpeed:0.00}m/s");
            }
        }

        float ComputeDeltaTime()
        {
            float now = Time.unscaledTime;
            float dt = m_LastDynamicTime < 0f ? Time.deltaTime : Mathf.Clamp(now - m_LastDynamicTime, 0f, 0.1f);
            m_LastDynamicTime = now;
            return dt;
        }

        public override void OnUnlink(XRGrabInteractable grabInteractable)
        {
            base.OnUnlink(grabInteractable);
            ClearGripState();
        }

        /// <summary>Last hand released: forget the interactors so IsTwoHanded/TwoHandBlend never report a stale grip.</summary>
        public void OnDrop(XRGrabInteractable grabInteractable, DropEventArgs args) => ClearGripState();

        void ClearGripState() { m_Primary = null; m_Support = null; m_HasState = false; m_TwoHandBlend = 0f; m_TwoHandTarget = 0f; m_Instability = 0f; m_HandoverPending = false; m_HandoverWeight = 0f; }
    }
}
