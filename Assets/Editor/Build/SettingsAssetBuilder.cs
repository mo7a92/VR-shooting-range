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
                // 9x19 mm service pistol (P38-style): ~360 m/s; semi-automatic only
                w.supportedFireModes = FireMode.Semi; w.defaultFireMode = FireMode.Semi; w.roundsPerMinute = 360f;
                w.magazineSize = 12; w.reloadSeconds = 1.5f; w.muzzleVelocity = 360f; w.spreadDegrees = 0.35f; w.gravityMultiplier = 1f;
                w.kickBackMetres = 0.045f; w.kickUpDegrees = 9f; w.kickSideDegrees = 1.0f; w.recoverySpeed = 14f; w.slideTravel = 0.03f;
                w.hapticAmplitude = 0.9f; w.hapticDuration = 0.05f; w.muzzleFlashScale = 0.8f; w.tracerColor = new Color(1f, 0.85f, 0.5f);
                Audio(w, "pistol_fire");
            });
            Rifle = Create<WeaponDefinition>($"{RangePaths.Settings}/Weapon_Rifle.asset", force, w =>
            {
                w.weaponId = "rifle"; w.displayName = "Rifle"; w.weaponClass = WeaponClass.Rifle; w.twoHanded = true;
                // 5.56x45 mm M4A1 carbine (14.5" barrel, M855): ~885 m/s, 700-950 rpm cyclic
                w.supportedFireModes = FireMode.Semi | FireMode.Auto; w.defaultFireMode = FireMode.Semi; w.roundsPerMinute = 800f;
                w.magazineSize = 30; w.reloadSeconds = 2.2f; w.muzzleVelocity = 885f; w.spreadDegrees = 0.2f; w.gravityMultiplier = 1f;
                w.kickBackMetres = 0.03f; w.kickUpDegrees = 4f; w.kickSideDegrees = 0.8f; w.recoverySpeed = 16f; w.slideTravel = 0.04f;
                w.hapticAmplitude = 1f; w.hapticDuration = 0.06f; w.secondaryHandHapticScale = 0.5f; w.muzzleFlashScale = 1.2f; w.tracerColor = new Color(1f, 0.6f, 0.3f);
                Audio(w, "rifle_fire");
            });
            Smg = Create<WeaponDefinition>($"{RangePaths.Settings}/Weapon_SMG.asset", force, w =>
            {
                w.weaponId = "smg"; w.displayName = "SMG"; w.weaponClass = WeaponClass.SubmachineGun; w.twoHanded = true;
                // 9x19 mm Micro Uzi: ~350 m/s, ~1,200 rpm cyclic
                w.supportedFireModes = FireMode.Auto | FireMode.Semi; w.defaultFireMode = FireMode.Auto; w.roundsPerMinute = 1200f;
                w.magazineSize = 32; w.reloadSeconds = 1.9f; w.muzzleVelocity = 350f; w.spreadDegrees = 0.9f; w.gravityMultiplier = 1f;
                w.kickBackMetres = 0.02f; w.kickUpDegrees = 2.6f; w.kickSideDegrees = 1.6f; w.recoverySpeed = 20f; w.slideTravel = 0.025f;
                w.hapticAmplitude = 0.8f; w.hapticDuration = 0.035f; w.secondaryHandHapticScale = 0.6f; w.muzzleFlashScale = 0.9f; w.tracerColor = new Color(1f, 0.9f, 0.6f);
                Audio(w, "smg_fire");
            });
            // Handling profiles are always (re)applied: they are tuned together with WeaponHandlingTransformer.
            // Headset round 1 (2026-11-04): every weapon has believable, distinct weight. The pistol is light but not weightless;
            // the SMG sits in between; the rifle is heavy and front-heavy, so one-handed it sags, lags, sways and loses control
            // under auto fire (still usable, owner's decision). Two hands make each of them steady.
            ApplyHandling(Pistol, mass: 0.9f, balance: 0.04f, oneHandResp: 0.06f, twoHandResp: 0.04f, sag1: 1.5f, sag2: 0.5f, sway1: 0.3f, sway2: 0.12f, posLag: 0.015f, rotLag: 5f, recoilUnsup: 1.3f, recoilSup: 0.75f, recUnsup: 9f, recSup: 14f, instab: 0.18f, decay: 3f, supportInfluence: 0f, maxPitch: 25f, maxYaw: 10f);
            ApplyHandling(Rifle, mass: 3.4f, balance: 0.28f, oneHandResp: 0.3f, twoHandResp: 0.04f, sag1: 22f, sag2: 0.6f, sway1: 1.6f, sway2: 0.15f, posLag: 0.04f, rotLag: 14f, recoilUnsup: 2.8f, recoilSup: 0.6f, recUnsup: 4.5f, recSup: 12f, instab: 0.35f, decay: 2f, supportInfluence: 0.85f, maxPitch: 30f, maxYaw: 14f);
            ApplyHandling(Smg, mass: 2.4f, balance: 0.14f, oneHandResp: 0.16f, twoHandResp: 0.04f, sag1: 9f, sag2: 0.5f, sway1: 0.9f, sway2: 0.15f, posLag: 0.03f, rotLag: 9f, recoilUnsup: 2.0f, recoilSup: 0.65f, recUnsup: 7f, recSup: 14f, instab: 0.25f, decay: 2.5f, supportInfluence: 0f, maxPitch: 25f, maxYaw: 12f); // cup hold like the pistol: no two-point aim
            BuildHandPoses(force);
            AssetDatabase.SaveAssets();
            SetupLog.Ok("ScoringRules, GameModeSettings, 3 WeaponDefinitions (with handling profiles) and hand poses ready in Assets/Settings");
        }

        static void ApplyHandling(WeaponDefinition w, float mass, float balance, float oneHandResp, float twoHandResp, float sag1, float sag2, float sway1, float sway2, float posLag, float rotLag, float recoilUnsup, float recoilSup, float recUnsup, float recSup, float instab, float decay, float supportInfluence, float maxPitch, float maxYaw)
        {
            if (w == null) return;
            if (w.slideRackClip == null) w.slideRackClip = Clip("Weapons/slide_rack");
            w.allowsSupportHand = true;
            w.virtualMassKg = mass; w.balanceForwardMetres = balance; w.oneHandResponseSeconds = oneHandResp; w.twoHandResponseSeconds = twoHandResp;
            w.oneHandSagDegrees = sag1; w.twoHandSagDegrees = sag2; w.oneHandSwayDegrees = sway1; w.twoHandSwayDegrees = sway2; w.swayFrequencyHz = 0.7f;
            w.maxPositionLagMetres = posLag; w.maxRotationLagDegrees = rotLag; w.supportHandInfluence = supportInfluence; w.gripTransitionSeconds = 0.2f;
            w.recoilUnsupportedMultiplier = recoilUnsup; w.recoilSupportedMultiplier = recoilSup; w.recoveryUnsupported = recUnsup; w.recoverySupported = recSup;
            w.instabilityPerShot = instab; w.instabilityDecayPerSecond = decay; w.maxRecoilPitchDegrees = maxPitch; w.maxRecoilYawDegrees = maxYaw;
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
            // Weapon poses are the *minimum* curl: HandPresence closes each finger further until it touches the grip collider
            // (contact solve), so these only pre-shape the hand. The index finger is not solved: it rests outside the trigger
            // guard and follows the trigger value.
            // Shooting-hand poses: thumb forward along the frame on the palm side (low curl, low spread), fingers pre-shaped
            // to half; the contact solve closes them onto the grip box.
            P("PistolGrip", 0.15f, 0.3f, 0.5f, 0.5f, 0.5f, 0.12f, true);
            P("PistolSupport", 0.1f, 0.45f, 0.45f, 0.5f, 0.6f, 0.3f, false); // thumb nearly straight: it lies forward along the frame (thumbs-forward grip)
            P("RiflePrimary", 0.15f, 0.3f, 0.5f, 0.5f, 0.5f, 0.12f, true);
            P("RifleSupport", 0.35f, 0.45f, 0.45f, 0.45f, 0.45f, 0.3f, false);
            P("SmgGrip", 0.15f, 0.3f, 0.5f, 0.5f, 0.5f, 0.12f, true);
            P("SmgSupport", 0.35f, 0.55f, 0.55f, 0.55f, 0.55f, 0.3f, false);
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
