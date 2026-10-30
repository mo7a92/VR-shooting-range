using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Scoring;
using VRRange.Targets;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// End-to-end checks on the generated ShootingRange scene without a headset: scene wiring, firing a weapon,
    /// projectile flight and target scoring. XR itself is not exercised here (no device in the test runner).
    /// </summary>
    public class RangeSceneSmokeTests
    {
        const string SceneName = "ShootingRange";

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.IsNotNull(load, $"Scene '{SceneName}' is not in the build settings; run VR Range > Setup first.");
            while (!load.isDone) yield return null;
            yield return null; // let Awake/Start run
        }

        [UnityTest]
        public IEnumerator SceneHasManagersLanesAndWeapons()
        {
            Assert.IsNotNull(ScoreManager.Instance, "ScoreManager missing");
            Assert.IsNotNull(GameModeManager.Instance, "GameModeManager missing");
            Assert.AreEqual(5, Object.FindObjectsByType<TargetLane>(FindObjectsSortMode.None).Length, "expected 5 lanes");
            var weapons = Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None);
            Assert.AreEqual(3, weapons.Length, "expected 3 weapons");
            CollectionAssert.AreEquivalent(new[] { WeaponClass.Pistol, WeaponClass.Rifle, WeaponClass.SubmachineGun }, weapons.Select(w => w.Definition.weaponClass));
            foreach (var w in weapons) Assert.AreEqual(w.MagazineSize, w.Rounds, $"{w.DisplayName} should start loaded");
            Assert.IsTrue(weapons.All(w => w.Muzzle != null));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PistolShotHitsTargetAndScores()
        {
            var gm = GameModeManager.Instance;
            gm.StartFreePractice();
            gm.SetPracticeDistance(10f);
            gm.SetPracticeMotion(TargetMotion.Static);
            yield return null;
            var lane = gm.Lanes.First(l => l != null && l.LaneIndex == 2);
            var pistol = Object.FindObjectsByType<Weapon>(FindObjectsSortMode.None).First(w => w.Definition.weaponClass == WeaponClass.Pistol);

            // Aim straight at the target face centre from the firing line, slightly above to allow for drop.
            var face = lane.Target.Face;
            var origin = new Vector3(face.position.x, face.position.y, 0f);
            var dir = (face.position + Vector3.up * 0.02f - origin).normalized;
            var rb = pistol.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
            pistol.transform.SetPositionAndRotation(origin - pistol.Muzzle.localPosition.z * dir, Quaternion.LookRotation(dir));

            var before = ScoreManager.Instance.Stats;
            Assert.IsTrue(pistol.TryFire(), "pistol should fire");
            float t = 0f;
            while (t < 2f && ScoreManager.Instance.Stats.hits == before.hits) { t += Time.deltaTime; yield return null; }
            var after = ScoreManager.Instance.Stats;
            Assert.AreEqual(before.shotsFired + 1, after.shotsFired, "shot not counted");
            Assert.AreEqual(before.hits + 1, after.hits, "projectile did not hit the target within 2 s");
            Assert.Greater(after.score, before.score, "hit did not score");
            Assert.AreEqual(1, lane.Target.HitsSinceReset);
            lane.ResetLane();
            Assert.AreEqual(0, lane.Target.HitsSinceReset);
        }

        [UnityTest]
        public IEnumerator AccuracyChallengeFinishesAfterShotLimit()
        {
            var gm = GameModeManager.Instance;
            var settings = gm.Settings;
            Assert.IsNotNull(settings);
            gm.StartAccuracyChallenge();
            float t = 0f;
            while (gm.State != ModeState.Running && t < 6f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(ModeState.Running, gm.State, "countdown did not finish");
            for (int i = 0; i < settings.accuracyChallengeShots; i++) ScoreManager.Instance.RegisterShot();
            t = 0f;
            while (gm.State != ModeState.Finished && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(ModeState.Finished, gm.State, "mode did not finish after the shot limit");
            Assert.AreEqual(settings.accuracyChallengeShots, gm.LastResult.stats.shotsFired);
        }

        [UnityTest]
        public IEnumerator MovingTargetsStayInsideLane()
        {
            var gm = GameModeManager.Instance;
            gm.SetDifficulty(Difficulty.Hard);
            gm.StartFreePractice();
            gm.SetPracticeMotion(TargetMotion.Random);
            var lanes = gm.Lanes.Where(l => l != null).ToList();
            for (int frame = 0; frame < 90; frame++)
            {
                foreach (var lane in lanes)
                {
                    var local = lane.Motion.Carrier.localPosition;
                    Assert.LessOrEqual(Mathf.Abs(local.x), 0.8f + 0.01f, "carrier left the lane laterally");
                    Assert.GreaterOrEqual(local.z, 4f - 0.01f);
                    Assert.LessOrEqual(local.z, 27.5f);
                }
                yield return null;
            }
        }
    }
}
