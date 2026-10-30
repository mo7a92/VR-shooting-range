using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Audio;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Scoring;

namespace VRRange.Weapons
{
    /// <summary>
    /// Grabbable weapon driven by XR Interaction Toolkit events. Trigger (XRI "Activate") fires, A/X reloads,
    /// B/Y toggles fire mode where supported. Recoil is purely visual on the weapon model plus controller haptics.
    /// Missing references degrade gracefully (the weapon still fires, just without that effect).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Weapon : MonoBehaviour
    {
        [SerializeField] WeaponDefinition m_Definition;
        [SerializeField] Transform m_Muzzle;
        [SerializeField] WeaponAnimator m_Animator;
        [SerializeField] ParticleSystem m_MuzzleFlash;
        [SerializeField] AudioSource m_AudioSource;
        [SerializeField] Light m_MuzzleLight;

        XRGrabInteractable m_Grab;
        FireMode m_FireMode = FireMode.Semi;
        int m_Rounds;
        bool m_TriggerHeld, m_Reloading, m_Warned;
        float m_NextFireTime;
        Coroutine m_ReloadRoutine;

        public event Action<Weapon> Fired;
        public event Action<Weapon> DryFired;
        public event Action<Weapon, int, int> AmmoChanged;      // weapon, rounds, magazineSize
        public event Action<Weapon, bool> ReloadStateChanged;  // weapon, isReloading
        public event Action<Weapon, FireMode> FireModeChanged;

        public WeaponDefinition Definition => m_Definition;
        public XRGrabInteractable Grab => m_Grab;
        public Transform Muzzle => m_Muzzle != null ? m_Muzzle : transform;
        public int Rounds => m_Rounds;
        public int MagazineSize => m_Definition != null ? m_Definition.magazineSize : 0;
        public bool IsReloading => m_Reloading;
        public bool IsHeld => m_Grab != null && m_Grab.isSelected && PrimaryHandInteractor != null;
        public FireMode CurrentFireMode => m_FireMode;
        public string DisplayName => m_Definition != null ? m_Definition.displayName : name;

        /// <summary>The first controller interactor holding the weapon (sockets are ignored).</summary>
        public XRBaseInputInteractor PrimaryHandInteractor
        {
            get
            {
                if (m_Grab == null) return null;
                var list = m_Grab.interactorsSelecting;
                for (int i = 0; i < list.Count; i++) if (list[i] is XRBaseInputInteractor input) return input;
                return null;
            }
        }

        public void Initialize(WeaponDefinition definition, Transform muzzle, WeaponAnimator animator, ParticleSystem muzzleFlash, AudioSource audioSource, Light muzzleLight)
        {
            m_Definition = definition; m_Muzzle = muzzle; m_Animator = animator; m_MuzzleFlash = muzzleFlash; m_AudioSource = audioSource; m_MuzzleLight = muzzleLight;
        }

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            if (m_Definition == null)
            {
                Debug.LogError($"[VRRange] Weapon '{name}' has no WeaponDefinition. It will not fire.", this);
                return;
            }
            m_FireMode = m_Definition.Supports(m_Definition.defaultFireMode) ? m_Definition.defaultFireMode : (m_Definition.Supports(FireMode.Semi) ? FireMode.Semi : FireMode.Auto);
            m_Rounds = m_Definition.magazineSize;
        }

        void OnEnable()
        {
            if (m_Grab == null) m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.activated.AddListener(OnActivated);
            m_Grab.deactivated.AddListener(OnDeactivated);
            m_Grab.selectExited.AddListener(OnSelectExited);
            m_Grab.selectEntered.AddListener(OnSelectEntered);
        }

        void OnDisable()
        {
            if (m_Grab == null) return;
            m_Grab.activated.RemoveListener(OnActivated);
            m_Grab.deactivated.RemoveListener(OnDeactivated);
            m_Grab.selectExited.RemoveListener(OnSelectExited);
            m_Grab.selectEntered.RemoveListener(OnSelectEntered);
            m_TriggerHeld = false;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRBaseInputInteractor)
                AmmoChanged?.Invoke(this, m_Rounds, MagazineSize);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (PrimaryHandInteractor == null) { m_TriggerHeld = false; m_Animator?.SetTriggerPulled(false); }
        }

        void OnActivated(ActivateEventArgs args)
        {
            if (!(args.interactorObject is XRBaseInputInteractor)) return;
            m_TriggerHeld = true;
            m_Animator?.SetTriggerPulled(true);
            TryFire(); // semi-auto: one shot per trigger pull
        }

        void OnDeactivated(DeactivateEventArgs args)
        {
            m_TriggerHeld = false;
            m_Animator?.SetTriggerPulled(false);
        }

        void Update()
        {
            if (m_Definition == null) return;
            var hand = PrimaryHandInteractor;
            if (hand == null) { m_TriggerHeld = false; return; }

            if (m_TriggerHeld && m_FireMode == FireMode.Auto) TryFire();

            var handedness = hand.handedness;
            if (WeaponInput.ReloadPressedThisFrame(handedness)) StartReload();
            if (WeaponInput.FireModePressedThisFrame(handedness)) ToggleFireMode();
        }

        public bool TryFire()
        {
            if (m_Definition == null || m_Reloading) return false;
            if (Time.time < m_NextFireTime) return false;
            if (m_Rounds <= 0)
            {
                m_NextFireTime = Time.time + 0.2f;
                DryFire();
                return false;
            }
            m_NextFireTime = Time.time + m_Definition.SecondsBetweenShots;
            Fire();
            return true;
        }

        void Fire()
        {
            m_Rounds--;
            var muzzle = Muzzle;
            var dir = BallisticsMath.ApplySpread(muzzle.forward, m_Definition.spreadDegrees, UnityEngine.Random.value, UnityEngine.Random.value);
            if (ProjectilePool.Instance != null)
            {
                var p = new ProjectileParams
                {
                    speed = m_Definition.muzzleVelocity, gravityMultiplier = m_Definition.gravityMultiplier,
                    lifetime = m_Definition.projectileLifetime, tracerColor = m_Definition.tracerColor
                };
                ProjectilePool.Instance.Fire(muzzle.position, dir, p, this);
            }
            else if (!m_Warned) { m_Warned = true; Debug.LogWarning("[VRRange] No ProjectilePool in scene; shots are counted but nothing flies.", this); }

            ScoreManager.NotifyShot();

            m_Animator?.PlayFire(m_Definition.kickBackMetres, m_Definition.kickUpDegrees, m_Definition.kickSideDegrees, m_Definition.recoverySpeed, m_Definition.slideTravel);
            if (m_MuzzleFlash != null) { m_MuzzleFlash.transform.localScale = Vector3.one * m_Definition.muzzleFlashScale; m_MuzzleFlash.Play(true); }
            if (m_MuzzleLight != null && isActiveAndEnabled) StartCoroutine(FlashLight());
            PlayClip(m_Definition.fireClip, m_Definition.fireVolume, 0.06f);
            SendHaptics(m_Definition.hapticAmplitude, m_Definition.hapticDuration);
            if (m_Rounds <= 0) m_Animator?.SetSlideLocked(true);

            Fired?.Invoke(this);
            AmmoChanged?.Invoke(this, m_Rounds, MagazineSize);
        }

        void DryFire()
        {
            PlayClip(m_Definition.dryFireClip, 0.7f, 0.05f);
            SendHaptics(0.15f, 0.03f);
            DryFired?.Invoke(this);
        }

        IEnumerator FlashLight()
        {
            m_MuzzleLight.enabled = true;
            yield return null; yield return null;
            m_MuzzleLight.enabled = false;
        }

        public void StartReload()
        {
            if (m_Definition == null || m_Reloading || m_Rounds >= m_Definition.magazineSize) return;
            m_ReloadRoutine = StartCoroutine(ReloadRoutine());
        }

        IEnumerator ReloadRoutine()
        {
            m_Reloading = true;
            ReloadStateChanged?.Invoke(this, true);
            PlayClip(m_Definition.reloadStartClip, 0.8f, 0.03f);
            m_Animator?.PlayReload(m_Definition.reloadSeconds);
            SendHaptics(0.3f, 0.05f);
            yield return new WaitForSeconds(m_Definition.reloadSeconds);
            m_Rounds = m_Definition.magazineSize;
            m_Animator?.SetSlideLocked(false);
            PlayClip(m_Definition.reloadEndClip, 0.8f, 0.03f);
            SendHaptics(0.4f, 0.06f);
            m_Reloading = false;
            ReloadStateChanged?.Invoke(this, false);
            AmmoChanged?.Invoke(this, m_Rounds, MagazineSize);
        }

        public void ToggleFireMode()
        {
            if (m_Definition == null) return;
            var next = m_FireMode == FireMode.Semi ? FireMode.Auto : FireMode.Semi;
            if (!m_Definition.Supports(next)) return;
            m_FireMode = next;
            PlayClip(m_Definition.fireModeClip, 0.6f, 0f);
            SendHaptics(0.2f, 0.04f);
            FireModeChanged?.Invoke(this, m_FireMode);
        }

        /// <summary>Refill without animation (used by mode restarts).</summary>
        public void RefillInstant()
        {
            if (m_ReloadRoutine != null) { StopCoroutine(m_ReloadRoutine); m_ReloadRoutine = null; }
            m_Reloading = false;
            m_Rounds = MagazineSize;
            m_Animator?.SetSlideLocked(false);
            AmmoChanged?.Invoke(this, m_Rounds, MagazineSize);
        }

        void PlayClip(AudioClip clip, float volume, float pitchJitter)
        {
            if (clip == null) return;
            if (m_AudioSource != null)
            {
                m_AudioSource.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
                m_AudioSource.PlayOneShot(clip, volume);
            }
            else AudioManager.PlayAt(clip, Muzzle.position, volume, pitchJitter);
        }

        void SendHaptics(float amplitude, float duration)
        {
            if (m_Grab == null || m_Definition == null) return;
            var list = m_Grab.interactorsSelecting;
            bool first = true;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is XRBaseInputInteractor input)
                {
                    float scale = first ? 1f : m_Definition.secondaryHandHapticScale;
                    input.SendHapticImpulse(Mathf.Clamp01(amplitude * scale), duration);
                    first = false;
                }
            }
        }
    }
}
