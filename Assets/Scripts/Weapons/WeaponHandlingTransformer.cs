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
    /// hand, weight-induced muzzle sag, natural sway, two-point aiming when the support hand grabs, smooth grip
    /// transitions and grip-dependent recoil impulses. Everything is applied to the weapon's target pose only; the
    /// tracked controllers and the camera are never touched, and lag is clamped so aiming stays possible.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponHandlingTransformer : XRBaseGrabTransformer
    {
        [SerializeField] WeaponDefinition m_Definition;
        [SerializeField] Transform m_PrimaryGrip;   // weapon-local primary grip (the XRGrabInteractable attach transform)
        [SerializeField] Transform m_SupportGrip;   // weapon-local support grip (secondary attach), optional
        [SerializeField] bool m_WeightSimulation = true;

        protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple;

        // grip state
        IXRSelectInteractor m_Primary, m_Support;
        Pose m_PrimaryGripLocal, m_SupportGripLocal;
        float m_TwoHandBlend;          // 0 = one hand, 1 = two hands
        float m_TwoHandTarget;
        bool m_HasState;
        // lag state
        Vector3 m_LagPos; Quaternion m_LagRot = Quaternion.identity;
        // recoil state (weapon-local): position back along -Z, pitch up, yaw
        Vector3 m_RecoilPos; Vector3 m_RecoilEuler;
        float m_Instability;
        float m_SwaySeed;
        float m_LastDynamicTime = -1f;
        Vector3 m_LastHandPos; float m_HandSpeed;

        public WeaponDefinition Definition { get => m_Definition; set => m_Definition = value; }
        public bool IsTwoHanded => m_Support != null;
        public float TwoHandBlend => m_TwoHandBlend;
        public float Instability => m_Instability;
        public float HandSpeed => m_HandSpeed;
        public bool WeightSimulation { get => m_WeightSimulation; set => m_WeightSimulation = value; }
        public Transform PrimaryGrip => m_PrimaryGrip;
        public Transform SupportGrip => m_SupportGrip;

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
            m_RecoilPos += new Vector3(0f, kickBackMetres * 0.1f, -kickBackMetres) * scale;
            m_RecoilEuler += new Vector3(-kickUpDegrees, Random.Range(-kickSideDegrees, kickSideDegrees), Random.Range(-kickSideDegrees, kickSideDegrees) * 0.4f) * scale;
            m_Instability = Mathf.Clamp01(m_Instability + m_Definition.instabilityPerShot * Mathf.Lerp(1f, 0.4f, m_TwoHandBlend));
        }

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            base.OnGrab(grabInteractable);
            RefreshInteractors(grabInteractable);
            m_HasState = false;
            m_RecoilPos = Vector3.zero; m_RecoilEuler = Vector3.zero;
        }

        public override void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose, Vector3 localScale)
        {
            base.OnGrabCountChanged(grabInteractable, targetPose, localScale);
            RefreshInteractors(grabInteractable);
        }

        void RefreshInteractors(XRGrabInteractable grab)
        {
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
            if (integrate && def != null)
            {
                float step = dt / Mathf.Max(0.02f, def.gripTransitionSeconds);
                m_TwoHandBlend = Mathf.MoveTowards(m_TwoHandBlend, m_TwoHandTarget, step);
            }
            if (m_Support != null && def != null)
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
            else if (m_TwoHandBlend > 0f && def != null)
            {
                // support hand released: blend back from the last two-handed rotation toward one-handed
                var rot = Quaternion.Slerp(ideal.rotation, m_LagRot, m_TwoHandBlend * 0.5f);
                ideal = new Pose(hand.position - rot * m_PrimaryGripLocal.position, rot);
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
                // recoil spring recovery
                float rec = WeaponHandlingMath.RecoverySpeed(def.recoveryUnsupported, def.recoverySupported, m_TwoHandBlend);
                float rk = 1f - Mathf.Exp(-rec * dt);
                m_RecoilPos = Vector3.Lerp(m_RecoilPos, Vector3.zero, rk);
                m_RecoilEuler = Vector3.Lerp(m_RecoilEuler, Vector3.zero, rk);
            }
            var pos = WeaponHandlingMath.ClampLag(ideal.position, m_LagPos, def.maxPositionLagMetres);
            var rotLag = WeaponHandlingMath.ClampLag(ideal.rotation, m_LagRot, def.maxRotationLagDegrees);
            if (integrate) { m_LagPos = pos; m_LagRot = rotLag; }

            // ---- weight sag + sway + recoil, all about the grip point ----
            float sag = WeaponHandlingMath.Sag(def.oneHandSagDegrees, def.twoHandSagDegrees, m_TwoHandBlend) * Mathf.Clamp(def.balanceForwardMetres / 0.1f, 0.3f, 2f);
            var sway = WeaponHandlingMath.Sway(Time.time, def.swayFrequencyHz, WeaponHandlingMath.SwayAmplitude(def.oneHandSwayDegrees, def.twoHandSwayDegrees, m_TwoHandBlend, m_Instability), m_SwaySeed);
            var localOffset = Quaternion.Euler(sag + sway.x + m_RecoilEuler.x, sway.y + m_RecoilEuler.y, m_RecoilEuler.z);
            var finalRot = rotLag * localOffset;
            // keep the grip point on the (lagged) hand position; recoil shifts it back along the barrel
            var gripWorld = pos + rotLag * m_PrimaryGripLocal.position; // where the grip currently is
            var finalPos = gripWorld - finalRot * m_PrimaryGripLocal.position + finalRot * m_RecoilPos;
            targetPose = new Pose(finalPos, finalRot);
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
            m_Primary = null; m_Support = null; m_HasState = false; m_TwoHandBlend = 0f; m_TwoHandTarget = 0f;
        }
    }
}
