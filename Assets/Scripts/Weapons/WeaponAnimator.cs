using UnityEngine;

namespace VRRange.Weapons
{
    /// <summary>
    /// Procedural weapon animation: recoil kick with spring recovery on the model root, slide/bolt cycling,
    /// trigger pull and magazine drop/insert. Everything is local to the weapon; the camera is never touched.
    /// </summary>
    public class WeaponAnimator : MonoBehaviour
    {
        [Header("Parts (optional, assigned by the prefab builder)")]
        [SerializeField] Transform m_ModelRoot;
        [SerializeField] Transform m_Slide;
        [SerializeField] Transform m_Trigger;
        [SerializeField] Transform m_Magazine;
        [SerializeField] Transform m_MagazineHome;

        [Header("Tuning")]
        [SerializeField] float m_SlideReturnSpeed = 18f;
        [SerializeField] float m_TriggerPullDegrees = 12f;
        [SerializeField] float m_MagazineDropDistance = 0.12f;

        Vector3 m_RootHomePos; Quaternion m_RootHomeRot;
        Vector3 m_SlideHomePos; Vector3 m_MagHomePos; Quaternion m_TriggerHomeRot;
        Vector3 m_KickPos; Vector3 m_KickEuler;
        float m_RecoverySpeed = 14f;
        float m_SlideOffset, m_SlideTarget, m_SlideTravel;
        bool m_SlideLocked, m_TriggerPulled;
        float m_MagProgress = 1f; // 1 = seated, 0 = fully out
        float m_MagSpeed = 1f;

        public Transform ModelRoot => m_ModelRoot;

        void Awake()
        {
            if (m_ModelRoot == null) m_ModelRoot = transform;
            m_RootHomePos = m_ModelRoot.localPosition; m_RootHomeRot = m_ModelRoot.localRotation;
            if (m_Slide != null) m_SlideHomePos = m_Slide.localPosition;
            if (m_Magazine != null) m_MagHomePos = m_MagazineHome != null ? m_MagazineHome.localPosition : m_Magazine.localPosition;
            if (m_Trigger != null) m_TriggerHomeRot = m_Trigger.localRotation;
        }

        public void Configure(Transform modelRoot, Transform slide, Transform trigger, Transform magazine)
        {
            m_ModelRoot = modelRoot; m_Slide = slide; m_Trigger = trigger; m_Magazine = magazine;
        }

        public void PlayFire(float kickBack, float kickUpDeg, float kickSideDeg, float recoverySpeed, float slideTravel)
        {
            m_RecoverySpeed = Mathf.Max(1f, recoverySpeed);
            m_KickPos += new Vector3(0f, kickBack * 0.15f, -kickBack);
            m_KickEuler += new Vector3(-kickUpDeg, Random.Range(-kickSideDeg, kickSideDeg), Random.Range(-kickSideDeg, kickSideDeg) * 0.5f);
            m_SlideTravel = slideTravel; m_SlideOffset = slideTravel; // snap back instantly, then return
        }

        public void SetSlideLocked(bool locked) { m_SlideLocked = locked; }
        public void SetTriggerPulled(bool pulled) { m_TriggerPulled = pulled; }

        public void PlayReload(float duration)
        {
            if (m_Magazine == null) return;
            m_MagSpeed = 2f / Mathf.Max(0.2f, duration); // half the time out, half the time in
            StopAllCoroutines();
            StartCoroutine(ReloadRoutine(duration));
        }

        System.Collections.IEnumerator ReloadRoutine(float duration)
        {
            float half = duration * 0.5f;
            float t = 0f;
            while (t < half) { t += Time.deltaTime; m_MagProgress = 1f - Mathf.Clamp01(t / half); yield return null; }
            m_MagProgress = 0f;
            t = 0f;
            while (t < half) { t += Time.deltaTime; m_MagProgress = Mathf.Clamp01(t / half); yield return null; }
            m_MagProgress = 1f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // Recoil spring recovery on the model root
            m_KickPos = Vector3.Lerp(m_KickPos, Vector3.zero, 1f - Mathf.Exp(-m_RecoverySpeed * dt));
            m_KickEuler = Vector3.Lerp(m_KickEuler, Vector3.zero, 1f - Mathf.Exp(-m_RecoverySpeed * dt));
            if (m_ModelRoot != null)
            {
                m_ModelRoot.localPosition = m_RootHomePos + m_RootHomeRot * m_KickPos;
                m_ModelRoot.localRotation = m_RootHomeRot * Quaternion.Euler(m_KickEuler);
            }
            if (m_Slide != null)
            {
                m_SlideTarget = m_SlideLocked ? m_SlideTravel : 0f;
                m_SlideOffset = Mathf.Lerp(m_SlideOffset, m_SlideTarget, 1f - Mathf.Exp(-m_SlideReturnSpeed * dt));
                m_Slide.localPosition = m_SlideHomePos + Vector3.back * m_SlideOffset;
            }
            if (m_Trigger != null)
            {
                var target = m_TriggerHomeRot * Quaternion.Euler(m_TriggerPulled ? m_TriggerPullDegrees : 0f, 0f, 0f);
                m_Trigger.localRotation = Quaternion.Slerp(m_Trigger.localRotation, target, 1f - Mathf.Exp(-25f * dt));
            }
            if (m_Magazine != null)
                m_Magazine.localPosition = m_MagHomePos + Vector3.down * (m_MagazineDropDistance * (1f - m_MagProgress));
        }
    }
}
