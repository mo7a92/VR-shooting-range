using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRRange.Audio;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Modes;
using VRRange.Scoring;
using VRRange.Targets;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>Edge cases: missing references and missing singletons must degrade gracefully (logged, never thrown).</summary>
    public class NullHandlingTests
    {
        [UnitySetUp]
        public IEnumerator EmptyScene()
        {
            SceneManager.LoadScene("ShootingRange", LoadSceneMode.Single); // ensure a known scene, then replace it with an empty one
            yield return null;
            var empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var unload = SceneManager.UnloadSceneAsync("ShootingRange");
            while (unload != null && !unload.isDone) yield return null;
            yield return null;
            Assert.IsNull(ScoreManager.Instance); Assert.IsNull(GameModeManager.Instance); Assert.IsNull(AudioManager.Instance); Assert.IsNull(ProjectilePool.Instance);
        }

        [UnityTest]
        public IEnumerator StaticManagersAreSafeWithoutInstances()
        {
            ScoreManager.NotifyShot();
            ScoreManager.NotifyHit(new HitResult { points = 10 });
            AudioManager.PlayAt(null, Vector3.zero); AudioManager.PlayUiClick(); AudioManager.PlayTargetDing(Vector3.zero); AudioManager.PlayCountdown(3); AudioManager.PlayRoundComplete(); AudioManager.PlayMenuConfirm();
            ImpactEffects.Spawn(default, Vector3.forward);
            ShellCasingPool.Eject(Vector3.zero, Vector3.up, Vector3.zero);
            Assert.AreEqual(0, GameModeManager.PersonalBest(GameMode.FreePractice, Difficulty.Easy));
            yield return null; // LogAssert fails the test automatically if anything above logged an error or threw
        }

        [UnityTest]
        public IEnumerator WeaponWithoutDefinitionLogsOnceAndNeverFires()
        {
            var go = new GameObject("BareWeapon"); go.AddComponent<Rigidbody>().isKinematic = true; go.AddComponent<BoxCollider>();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("has no WeaponDefinition"));
            var weapon = go.AddComponent<Weapon>();
            yield return null;
            Assert.IsFalse(weapon.TryFire()); Assert.AreEqual(0, weapon.MagazineSize); Assert.AreEqual(0, weapon.Rounds);
            weapon.StartReload(); weapon.ToggleFireMode(); weapon.RefillInstant();
            Assert.IsNull(weapon.GetGripAnchorFor(null));
            Assert.IsFalse(weapon.IsHeld);
            yield return null;
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator WeaponWithDefinitionButNoPoolOrEffectsStillCountsShots()
        {
            var go = new GameObject("Managers"); go.AddComponent<ScoreManager>();
            var def = ScriptableObject.CreateInstance<WeaponDefinition>(); def.magazineSize = 3; def.roundsPerMinute = 1500f;
            var wgo = new GameObject("Weapon"); wgo.SetActive(false); // configure before Awake runs
            wgo.AddComponent<Rigidbody>().isKinematic = true; wgo.AddComponent<BoxCollider>();
            var grab = wgo.AddComponent<XRGrabInteractable>(); grab.enabled = false;
            var weapon = wgo.AddComponent<Weapon>();
            weapon.Initialize(def, null, null, null, null, null);
            wgo.SetActive(true);
            yield return null;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("No ProjectilePool"));
            Assert.IsTrue(weapon.TryFire(), "fires without pool/effects/audio");
            Assert.AreEqual(1, ScoreManager.Instance.Stats.shotsFired);
            yield return XrTestRig.WaitSeconds(0.1f);
            Assert.IsTrue(weapon.TryFire(), "second shot after the rate limit");
            yield return XrTestRig.WaitSeconds(0.1f);
            Assert.IsTrue(weapon.TryFire(), "third shot");
            Assert.AreEqual(0, weapon.Rounds);
            yield return XrTestRig.WaitSeconds(0.1f);
            Assert.IsFalse(weapon.TryFire(), "empty");
            Object.Destroy(go); Object.Destroy(wgo); Object.Destroy(def);
        }

        [UnityTest]
        public IEnumerator ProjectilePoolWithoutPrefabLogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no projectile prefab"));
            var go = new GameObject("Pool"); var pool = go.AddComponent<ProjectilePool>();
            yield return null;
            Assert.IsNull(pool.Fire(Vector3.zero, Vector3.forward, new ProjectileParams { speed = 1f, lifetime = 1f }, null));
            pool.Release(null);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator GameModeManagerWithoutLanesWeaponsOrSettingsRunsAndFinishes()
        {
            var go = new GameObject("GM"); go.AddComponent<ScoreManager>(); var gm = go.AddComponent<GameModeManager>();
            yield return null; yield return null;
            Assert.AreEqual(ModeState.Running, gm.State, "free practice starts even with nothing wired");
            gm.SetDifficulty(Difficulty.Hard); gm.SetPracticeDistance(15f); gm.SetPracticeMotion(TargetMotion.Random); gm.SetPracticeDistanceIndex(2); gm.ResetTargets();
            gm.StartAccuracyChallenge(); gm.Finish();
            Assert.AreEqual(ModeState.Finished, gm.State); Assert.AreEqual(0, gm.LastResult.maxPossibleScore); Assert.AreEqual("-", gm.LastResult.grade);
            gm.StartMovingTargetChallenge(); gm.Finish(); gm.Restart();
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator TargetWithoutRulesOrMarkerStillReportsHits()
        {
            var go = new GameObject("Target"); var target = go.AddComponent<BullseyeTarget>();
            HitResult? got = null; target.Hit += (_, r) => got = r;
            target.OnProjectileHit(new HitInfo { point = Vector3.zero, normal = Vector3.back });
            Assert.IsTrue(got.HasValue); Assert.AreEqual(0, got.Value.points); Assert.AreEqual("Hit", got.Value.ringName);
            Assert.AreEqual(1, target.HitsSinceReset);
            target.ResetTarget(); Assert.AreEqual(0, target.HitsSinceReset);
            var lane = go.AddComponent<TargetLane>();
            lane.SetDistance(10f); lane.SetMotion(TargetMotion.PopUp, Difficulty.Hard); lane.ResetLane();
            yield return null;
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator MovingTargetControllerWithoutCarrierOrProfileClampsAndMoves()
        {
            var go = new GameObject("Motion"); var m = go.AddComponent<MovingTargetController>();
            m.Initialize(null, null, 1f, 5f, 25f);
            m.SetBaseDistance(100f); // clamped to far
            Assert.AreEqual(25f, m.Carrier.localPosition.z, 0.001f);
            m.Configure(TargetMotion.Random, null);
            m.Configure(TargetMotion.Horizontal, null);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.LessOrEqual(Mathf.Abs(m.Carrier.localPosition.x), 1.0001f);
            Assert.IsTrue(m.IsExposed);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator HandPresenceWithoutRigWarnsAndStaysPassive()
        {
            var go = new GameObject("Hand"); var model = new GameObject("Model"); model.transform.SetParent(go.transform);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no wrist joint"));
            var hand = go.AddComponent<HandPresence>();
            yield return null; yield return null;
            Assert.IsFalse(hand.IsCalibrated);
            Assert.IsFalse(hand.TryGetTip(HandPresence.Finger.Index, out _));
            hand.OverridePose(HandPose.Create("x", 1, 1, 1, 1, 1, 0.5f, true), 1f);
            hand.SetInputOverride(1f, 1f);
            yield return null;
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator HandlingTransformerWithoutDefinitionLeavesPoseUntouched()
        {
            var go = new GameObject("W"); go.AddComponent<Rigidbody>().isKinematic = true; go.AddComponent<BoxCollider>();
            var grab = go.AddComponent<XRGrabInteractable>(); grab.enabled = false;
            var t = go.AddComponent<WeaponHandlingTransformer>();
            var pose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f)); var scale = Vector3.one;
            t.Process(grab, UnityEngine.XR.Interaction.Toolkit.XRInteractionUpdateOrder.UpdatePhase.Dynamic, ref pose, ref scale);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), pose.position);
            t.AddRecoilImpulse(1f, 1f, 1f); // no definition: ignored
            Assert.AreEqual(0f, t.Instability);
            yield return null;
            Object.Destroy(go);
        }
    }
}
