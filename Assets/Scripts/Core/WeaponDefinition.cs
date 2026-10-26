using UnityEngine;

namespace VRRange.Core
{
    public enum WeaponClass { Pistol, Rifle, SubmachineGun }

    [System.Flags]
    public enum FireMode { None = 0, Semi = 1, Auto = 2 }

    /// <summary>
    /// Tunable, fictional gameplay values for one weapon. All numbers are game-design values chosen for a
    /// readable VR range experience; they are not real-world firearm data.
    /// </summary>
    [CreateAssetMenu(menuName = "VR Range/Weapon Definition", fileName = "Weapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string weaponId = "pistol";
        public string displayName = "Pistol";
        public WeaponClass weaponClass = WeaponClass.Pistol;
        [Tooltip("Rifle and SMG support a second grip; pistol is one-handed.")]
        public bool twoHanded;

        [Header("Firing (fictional game values)")]
        public FireMode supportedFireModes = FireMode.Semi;
        public FireMode defaultFireMode = FireMode.Semi;
        [Tooltip("Shots per minute in automatic / repeated fire.")]
        [Range(60, 1500)] public float roundsPerMinute = 300f;
        [Range(1, 100)] public int magazineSize = 12;
        [Range(0.3f, 6f)] public float reloadSeconds = 1.6f;
        [Tooltip("Projectile speed in metres per second (scaled down so flight is visible).")]
        [Range(20f, 400f)] public float muzzleVelocity = 90f;
        [Tooltip("Random cone half-angle in degrees applied per shot.")]
        [Range(0f, 5f)] public float spreadDegrees = 0.35f;
        [Tooltip("Multiplier on gravity for a visible drop at range.")]
        [Range(0f, 10f)] public float gravityMultiplier = 3f;
        [Tooltip("Seconds a projectile may fly before it is recycled.")]
        [Range(0.5f, 10f)] public float projectileLifetime = 3f;

        [Header("Visual recoil (weapon model only, never the camera)")]
        [Range(0f, 0.2f)] public float kickBackMetres = 0.03f;
        [Range(0f, 30f)] public float kickUpDegrees = 4f;
        [Range(0f, 10f)] public float kickSideDegrees = 0.6f;
        [Range(1f, 40f)] public float recoverySpeed = 14f;
        [Tooltip("How far the slide / bolt travels back on each shot (metres).")]
        [Range(0f, 0.1f)] public float slideTravel = 0.03f;

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
