using UnityEngine;

namespace VRRange.Core
{
    // new classes are appended so serialized assets keep their values
    public enum WeaponClass { Pistol, Rifle, SubmachineGun, AssaultRifle, SniperRifle }

    [System.Flags]
    public enum FireMode { None = 0, Semi = 1, Auto = 2 }

    /// <summary>
    /// Tunable values for one weapon. Ballistics (muzzle velocity, cyclic rate) use real-world figures for the
    /// modelled firearms since the headset round of 2026-11-04; handling, recoil and haptic numbers are game values
    /// tuned for feel. Everything is editable on the assets in Assets/Settings.
    /// </summary>
    [CreateAssetMenu(menuName = "VR Range/Weapon Definition", fileName = "Weapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string weaponId = "pistol";
        public string displayName = "Pistol";
        public WeaponClass weaponClass = WeaponClass.Pistol;
        [Tooltip("Rifle and SMG have a separate support grip (handguard) that steers the aim (two-point aiming).")]
        public bool twoHanded;
        [Tooltip("A second hand may grab the weapon as a support hand (steadier aim, less recoil). On the pistol it cups the shooting hand and takes the weapon over when the shooting hand lets go.")]
        public bool allowsSupportHand = true;

        [Header("Firing")]
        public FireMode supportedFireModes = FireMode.Semi;
        public FireMode defaultFireMode = FireMode.Semi;
        [Tooltip("Manual bolt action: after every shot the bolt must be cycled (B / Y, or Weapon.CycleBolt) before the next one fires; the empty case is ejected when the bolt comes back.")]
        public bool boltAction;
        [Tooltip("Seconds for a full bolt cycle (lift, back, forward, down).")]
        [Range(0.2f, 2f)] public float boltCycleSeconds = 0.65f;
        [Tooltip("Shots per minute in automatic / repeated fire.")]
        [Range(60, 1500)] public float roundsPerMinute = 300f;
        [Range(1, 100)] public int magazineSize = 12;
        [Range(0.3f, 6f)] public float reloadSeconds = 1.6f;
        [Tooltip("Projectile speed in metres per second (real values: 9x19 mm pistol ~360, 5.56 mm carbine ~885, 9 mm SMG ~350).")]
        [Range(20f, 1200f)] public float muzzleVelocity = 360f;
        [Tooltip("Random cone half-angle in degrees applied per shot.")]
        [Range(0f, 5f)] public float spreadDegrees = 0.35f;
        [Tooltip("Multiplier on gravity (1 = real drop; a few millimetres at 25 m for these velocities).")]
        [Range(0f, 10f)] public float gravityMultiplier = 1f;
        [Tooltip("Seconds a projectile may fly before it is recycled.")]
        [Range(0.5f, 10f)] public float projectileLifetime = 3f;

        [Header("Visual recoil (weapon model only, never the camera)")]
        [Range(0f, 0.2f)] public float kickBackMetres = 0.03f;
        [Range(0f, 30f)] public float kickUpDegrees = 4f;
        [Range(0f, 10f)] public float kickSideDegrees = 0.6f;
        [Range(1f, 40f)] public float recoverySpeed = 14f;
        [Tooltip("How far the slide / bolt travels back on each shot (metres).")]
        [Range(0f, 0.1f)] public float slideTravel = 0.03f;

        [Header("Weight & handling (game values, see WeaponHandlingTransformer)")]
        [Tooltip("Virtual mass in kg. Drives lag, sag, sway and recoil scaling (pistol ~0.9, SMG ~2.4, rifle ~3.4).")]
        [Range(0.2f, 8f)] public float virtualMassKg = 1f;
        [Tooltip("Centre of mass offset from the primary grip along the barrel (metres). Larger = more muzzle-heavy.")]
        [Range(0f, 0.6f)] public float balanceForwardMetres = 0.05f;
        [Tooltip("Time constant (s) for the weapon to follow the hand when held one-handed. Higher = more inertia.")]
        [Range(0.01f, 0.5f)] public float oneHandResponseSeconds = 0.05f;
        [Tooltip("Time constant (s) when supported by the second hand.")]
        [Range(0.01f, 0.4f)] public float twoHandResponseSeconds = 0.03f;
        [Tooltip("Muzzle drop in degrees when held one-handed (weight-induced sag).")]
        [Range(0f, 30f)] public float oneHandSagDegrees = 1f;
        [Tooltip("Muzzle drop in degrees when supported by both hands.")]
        [Range(0f, 10f)] public float twoHandSagDegrees = 0.2f;
        [Tooltip("Natural sway amplitude in degrees when held one-handed and still.")]
        [Range(0f, 4f)] public float oneHandSwayDegrees = 0.25f;
        [Tooltip("Sway amplitude in degrees when two-handed.")]
        [Range(0f, 3f)] public float twoHandSwayDegrees = 0.08f;
        [Range(0.1f, 3f)] public float swayFrequencyHz = 0.7f;
        [Tooltip("Maximum lag of the grip behind the hand (metres) so the weapon never detaches visually.")]
        [Range(0.005f, 0.08f)] public float maxPositionLagMetres = 0.02f;
        [Tooltip("Maximum rotational lag behind the hand (degrees).")]
        [Range(1f, 30f)] public float maxRotationLagDegrees = 6f;
        [Tooltip("How strongly the support hand steers the aim when two-handed (0 = primary hand only, 1 = fully two-point aim).")]
        [Range(0f, 1f)] public float supportHandInfluence = 0.85f;
        [Tooltip("Seconds to blend between one- and two-handed handling.")]
        [Range(0.02f, 0.6f)] public float gripTransitionSeconds = 0.18f;
        [Tooltip("Recoil multiplier when unsupported (one hand).")]
        [Range(0.2f, 4f)] public float recoilUnsupportedMultiplier = 1.3f;
        [Tooltip("Recoil multiplier when supported by both hands.")]
        [Range(0.1f, 2f)] public float recoilSupportedMultiplier = 0.65f;
        [Tooltip("Recovery speed of the recoil spring when unsupported.")]
        [Range(1f, 40f)] public float recoveryUnsupported = 9f;
        [Tooltip("Recovery speed of the recoil spring when supported.")]
        [Range(1f, 40f)] public float recoverySupported = 16f;
        [Tooltip("Instability added per shot (increases sway and lag briefly); decays over time.")]
        [Range(0f, 1f)] public float instabilityPerShot = 0.25f;
        [Range(0.5f, 10f)] public float instabilityDecayPerSecond = 2.5f;
        [Tooltip("Maximum accumulated recoil pitch (degrees) so auto fire climbs a lot but never flips the weapon.")]
        [Range(5f, 60f)] public float maxRecoilPitchDegrees = 30f;
        [Tooltip("Maximum accumulated recoil yaw (degrees): the random horizontal walk during auto fire is clamped to this.")]
        [Range(2f, 30f)] public float maxRecoilYawDegrees = 14f;

        [Header("Haptics")]
        [Range(0f, 1f)] public float hapticAmplitude = 0.7f;
        [Range(0f, 0.5f)] public float hapticDuration = 0.08f;
        [Range(0f, 1f)] public float secondaryHandHapticScale = 0.5f;

        [Header("Audio")]
        public AudioClip fireClip;
        public AudioClip dryFireClip;
        public AudioClip reloadStartClip;
        public AudioClip reloadEndClip;
        public AudioClip fireModeClip;
        public AudioClip slideRackClip;
        [Range(0f, 1f)] public float fireVolume = 1f;

        [Header("Effects")]
        [Range(0.2f, 3f)] public float muzzleFlashScale = 1f;
        public Color tracerColor = new Color(1f, 0.85f, 0.5f, 1f);

        public float SecondsBetweenShots => 60f / Mathf.Max(1f, roundsPerMinute);
        public bool Supports(FireMode mode) => (supportedFireModes & mode) != 0;

        void OnValidate()
        {
            if (!Supports(defaultFireMode))
                defaultFireMode = Supports(FireMode.Semi) ? FireMode.Semi : FireMode.Auto;
            if (weaponClass != WeaponClass.Pistol) twoHanded = true;
        }
    }
}
