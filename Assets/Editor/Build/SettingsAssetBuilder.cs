using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRRange.Core;
using VRRange.Hands;

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
        static readonly Dictionary<string, HandPose> s_Poses = new Dictionary<string, HandPose>();
        public static HandPose Pose(string name) => s_Poses.TryGetValue(name ?? "", out var p) ? p : (s_Poses.TryGetValue("Fist", out var f) ? f : null);

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
            // Handling profiles are always (re)applied: they are tuned together with WeaponHandlingTransformer.
            ApplyHandling(Pistol, mass: 0.9f, balance: 0.04f, oneHandResp: 0.035f, twoHandResp: 0.03f, sag1: 0.6f, sag2: 0.2f, sway1: 0.18f, sway2: 0.08f, posLag: 0.012f, rotLag: 4f, recoilUnsup: 1.1f, recoilSup: 0.8f, recUnsup: 14f, recSup: 18f, instab: 0.15f);
            ApplyHandling(Rifle, mass: 3.4f, balance: 0.28f, oneHandResp: 0.14f, twoHandResp: 0.035f, sag1: 4.5f, sag2: 0.35f, sway1: 0.7f, sway2: 0.1f, posLag: 0.03f, rotLag: 9f, recoilUnsup: 1.6f, recoilSup: 0.55f, recUnsup: 6f, recSup: 16f, instab: 0.3f);
            ApplyHandling(Smg, mass: 2.4f, balance: 0.14f, oneHandResp: 0.08f, twoHandResp: 0.035f, sag1: 2.2f, sag2: 0.3f, sway1: 0.4f, sway2: 0.1f, posLag: 0.02f, rotLag: 6f, recoilUnsup: 1.35f, recoilSup: 0.65f, recUnsup: 9f, recSup: 17f, instab: 0.22f);
            BuildHandPoses(force);
            AssetDatabase.SaveAssets();
            SetupLog.Ok("ScoringRules, GameModeSettings, 3 WeaponDefinitions (with handling profiles) and hand poses ready in Assets/Settings");
        }

        static void ApplyHandling(WeaponDefinition w, float mass, float balance, float oneHandResp, float twoHandResp, float sag1, float sag2, float sway1, float sway2, float posLag, float rotLag, float recoilUnsup, float recoilSup, float recUnsup, float recSup, float instab)
        {
            if (w == null) return;
            if (w.slideRackClip == null) w.slideRackClip = Clip("Weapons/slide_rack");
            w.virtualMassKg = mass; w.balanceForwardMetres = balance; w.oneHandResponseSeconds = oneHandResp; w.twoHandResponseSeconds = twoHandResp;
            w.oneHandSagDegrees = sag1; w.twoHandSagDegrees = sag2; w.oneHandSwayDegrees = sway1; w.twoHandSwayDegrees = sway2; w.swayFrequencyHz = 0.7f;
            w.maxPositionLagMetres = posLag; w.maxRotationLagDegrees = rotLag; w.supportHandInfluence = 0.85f; w.gripTransitionSeconds = 0.18f;
            w.recoilUnsupportedMultiplier = recoilUnsup; w.recoilSupportedMultiplier = recoilSup; w.recoveryUnsupported = recUnsup; w.recoverySupported = recSup;
            w.instabilityPerShot = instab; w.instabilityDecayPerSecond = 2.5f;
            EditorUtility.SetDirty(w);
        }

        static void BuildHandPoses(bool force)
        {
            s_Poses.Clear();
            RangePaths.EnsureFolder(RangePaths.Settings + "/HandPoses");
            void P(string name, float thumb, float index, float middle, float ring, float little, float spread, bool trig)
            {
                var path = $"{RangePaths.Settings}/HandPoses/{name}.asset";
                var pose = AssetDatabase.LoadAssetAtPath<HandPose>(path);
                if (pose == null) { pose = HandPose.Create(name, thumb, index, middle, ring, little, spread, trig); AssetDatabase.CreateAsset(pose, path); }
                else if (force) { pose.thumb = thumb; pose.index = index; pose.middle = middle; pose.ring = ring; pose.little = little; pose.thumbSpread = spread; pose.indexFollowsTrigger = trig; EditorUtility.SetDirty(pose); }
                s_Poses[name] = pose;
            }
            P("Open", 0.05f, 0.0f, 0.0f, 0.0f, 0.0f, 0.6f, false);
            P("Idle", 0.3f, 0.15f, 0.2f, 0.25f, 0.3f, 0.45f, false);
            P("Fist", 0.9f, 0.95f, 0.95f, 0.95f, 0.95f, 0.1f, false);
            P("Pinch", 0.6f, 0.65f, 0.8f, 0.85f, 0.9f, 0.2f, false);
            P("Point", 0.8f, 0.0f, 0.95f, 0.95f, 0.95f, 0.2f, false);
            P("PistolGrip", 0.55f, 0.35f, 0.9f, 0.92f, 0.94f, 0.25f, true);
            P("RiflePrimary", 0.6f, 0.35f, 0.9f, 0.92f, 0.94f, 0.2f, true);
            P("RifleSupport", 0.7f, 0.75f, 0.8f, 0.82f, 0.85f, 0.15f, false);
            P("SmgGrip", 0.55f, 0.35f, 0.9f, 0.92f, 0.94f, 0.25f, true);
        }

        static void Audio(WeaponDefinition w, string fireClip)
        {
            w.fireClip = Clip($"Weapons/{fireClip}");
            w.dryFireClip = Clip("Weapons/dry_fire");
            w.reloadStartClip = Clip("Weapons/mag_out");
            w.reloadEndClip = Clip("Weapons/mag_in");
            w.fireModeClip = Clip("Weapons/fire_mode_switch");
            w.slideRackClip = Clip("Weapons/slide_rack");
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
