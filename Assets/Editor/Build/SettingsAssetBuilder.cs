using UnityEditor;
using UnityEngine;
using VRRange.Core;

namespace VRRange.Editor
{
    /// <summary>Creates the tunable ScriptableObjects (weapon definitions, scoring rules, mode settings). Values are only written on creation or when forced.</summary>
    public static class SettingsAssetBuilder
    {
        public static ScoringRules Rules { get; private set; }
        public static GameModeSettings ModeSettings { get; private set; }
        public static WeaponDefinition Pistol { get; private set; }
        public static WeaponDefinition Rifle { get; private set; }
        public static WeaponDefinition Smg { get; private set; }

        public static void Build(bool force)
        {
            SetupLog.Section("Settings assets");
            RangePaths.EnsureFolder(RangePaths.Settings);
            Rules = Create<ScoringRules>($"{RangePaths.Settings}/ScoringRules.asset", force, r => { });
            ModeSettings = Create<GameModeSettings>($"{RangePaths.Settings}/GameModeSettings.asset", force, m => { });
            Pistol = Create<WeaponDefinition>($"{RangePaths.Settings}/Weapon_Pistol.asset", force, w =>
            {
                w.weaponId = "pistol"; w.displayName = "Pistol"; w.weaponClass = WeaponClass.Pistol; w.twoHanded = false;
                w.supportedFireModes = FireMode.Semi; w.defaultFireMode = FireMode.Semi; w.roundsPerMinute = 360f;
                w.magazineSize = 12; w.reloadSeconds = 1.5f; w.muzzleVelocity = 90f; w.spreadDegrees = 0.35f; w.gravityMultiplier = 3f;
                w.kickBackMetres = 0.035f; w.kickUpDegrees = 6f; w.kickSideDegrees = 0.8f; w.recoverySpeed = 16f; w.slideTravel = 0.03f;
                w.hapticAmplitude = 0.65f; w.hapticDuration = 0.07f; w.muzzleFlashScale = 0.8f; w.tracerColor = new Color(1f, 0.85f, 0.5f);
                Audio(w, "pistol_fire");
            });
            Rifle = Create<WeaponDefinition>($"{RangePaths.Settings}/Weapon_Rifle.asset", force, w =>
            {
                w.weaponId = "rifle"; w.displayName = "Rifle"; w.weaponClass = WeaponClass.Rifle; w.twoHanded = true;
                w.supportedFireModes = FireMode.Semi | FireMode.Auto; w.defaultFireMode = FireMode.Semi; w.roundsPerMinute = 600f;
                w.magazineSize = 30; w.reloadSeconds = 2.2f; w.muzzleVelocity = 180f; w.spreadDegrees = 0.2f; w.gravityMultiplier = 2f;
                w.kickBackMetres = 0.02f; w.kickUpDegrees = 2.5f; w.kickSideDegrees = 0.5f; w.recoverySpeed = 20f; w.slideTravel = 0.04f;
                w.hapticAmplitude = 0.8f; w.hapticDuration = 0.06f; w.secondaryHandHapticScale = 0.5f; w.muzzleFlashScale = 1.2f; w.tracerColor = new Color(1f, 0.6f, 0.3f);
                Audio(w, "rifle_fire");
            });
            Smg = Create<WeaponDefinition>($"{RangePaths.Settings}/Weapon_SMG.asset", force, w =>
            {
                w.weaponId = "smg"; w.displayName = "SMG"; w.weaponClass = WeaponClass.SubmachineGun; w.twoHanded = true;
                w.supportedFireModes = FireMode.Auto | FireMode.Semi; w.defaultFireMode = FireMode.Auto; w.roundsPerMinute = 850f;
                w.magazineSize = 32; w.reloadSeconds = 1.9f; w.muzzleVelocity = 110f; w.spreadDegrees = 0.9f; w.gravityMultiplier = 3f;
                w.kickBackMetres = 0.015f; w.kickUpDegrees = 1.8f; w.kickSideDegrees = 1.2f; w.recoverySpeed = 24f; w.slideTravel = 0.025f;
                w.hapticAmplitude = 0.5f; w.hapticDuration = 0.04f; w.secondaryHandHapticScale = 0.6f; w.muzzleFlashScale = 0.9f; w.tracerColor = new Color(1f, 0.9f, 0.6f);
                Audio(w, "smg_fire");
            });
            AssetDatabase.SaveAssets();
            SetupLog.Ok("ScoringRules, GameModeSettings and 3 WeaponDefinitions ready in Assets/Settings");
        }

        static void Audio(WeaponDefinition w, string fireClip)
        {
            w.fireClip = Clip($"Weapons/{fireClip}");
            w.dryFireClip = Clip("Weapons/dry_fire");
            w.reloadStartClip = Clip("Weapons/mag_out");
            w.reloadEndClip = Clip("Weapons/mag_in");
            w.fireModeClip = Clip("Weapons/fire_mode_switch");
        }

        public static AudioClip Clip(string relative)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RangePaths.Audio}/{relative}.wav");
            if (c == null) SetupLog.Warn($"Audio clip missing: {relative}.wav (run Tools/AssetGen/gen_audio.py)");
            return c;
        }

        static T Create<T>(string path, bool force, System.Action<T> init) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null && !force) return existing;
            if (existing == null)
            {
                existing = ScriptableObject.CreateInstance<T>();
                init(existing);
                AssetDatabase.CreateAsset(existing, path);
            }
            else { init(existing); EditorUtility.SetDirty(existing); }
            return existing;
        }
    }
}
