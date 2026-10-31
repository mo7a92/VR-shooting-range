using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Scoring;
using VRRange.Targets;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>Projectile flight, pooling, hit detection, impact effects, and every target motion/difficulty setting.</summary>
    public class ProjectileAndTargetTests
    {
        [UnitySetUp] public IEnumerator Setup() => XrTestRig.LoadScene();

        static TargetLane Lane(int index) => GameModeManager.Instance.Lanes.First(l => l != null && l.LaneIndex == index);

        [UnityTest]
        public IEnumerator ProjectilesAreRecycledAndNeverPersist()
        {
            var gm = GameModeManager.Instance; gm.StartFreePractice(); gm.SetPracticeMotion(TargetMotion.Static); yield return null;
            var rifle = XrTestRig.Find(WeaponClass.Rifle);
            var all = Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.GreaterOrEqual(all, 8, "pool is prewarmed");
            // shoot past the lane-3 target (x = 0.45 m clears the 0.6 m face) into the backstop at z = 29 m
            XrTestRig.Aim(rifle, new Vector3(0.45f, 1.4f, 0f), new Vector3(0.45f, 1.6f, 29f));
            for (int i = 0; i < 40; i++) { rifle.RefillInstant(); rifle.TryFire(); if (i == 39) Assert.Greater(XrTestRig.ActiveProjectiles(), 0, "a projectile is in flight right after a shot"); yield return XrTestRig.WaitSeconds(rifle.Definition.SecondsBetweenShots + 0.01f); }
            yield return XrTestRig.WaitUntil(() => XrTestRig.ActiveProjectiles() == 0, rifle.Definition.projectileLifetime + 2f, "all projectiles to land or expire");
            int after = Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.LessOrEqual(after, Mathf.Max(all, 40) + 8, "the pool grows at most to the burst size; no unbounded instantiation");
            // a second burst reuses instances
            for (int i = 0; i < 40; i++) { rifle.RefillInstant(); rifle.TryFire(); yield return XrTestRig.WaitSeconds(rifle.Definition.SecondsBetweenShots + 0.01f); }
            yield return XrTestRig.WaitUntil(() => XrTestRig.ActiveProjectiles() == 0, rifle.Definition.projectileLifetime + 2f, "second burst to finish");
            Assert.AreEqual(after, Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "pool size stable after reuse");
        }

        [UnityTest]
        public IEnumerator ProjectileExpiresByLifetimeWhenNothingIsHit()
        {
            // The room is closed, so every real shot eventually hits something; verify the lifetime rule with a slow,
            // short-lived projectile that cannot reach any surface before it expires.
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var proj = ProjectilePool.Instance.Fire(new Vector3(0f, 1.4f, 2f), Vector3.forward, new ProjectileParams { speed = 1f, gravityMultiplier = 0f, lifetime = 0.5f, tracerColor = Color.white }, pistol);
            Assert.IsNotNull(proj); Assert.IsTrue(proj.IsActive);
            yield return XrTestRig.WaitSeconds(0.3f);
            Assert.IsTrue(proj.IsActive, "still flying before its lifetime ends");
            yield return XrTestRig.WaitSeconds(0.5f);
            Assert.IsFalse(proj.IsActive, "recycled after its lifetime");
        }

        [UnityTest]
        public IEnumerator GravityDropsTheProjectileOverDistance()
        {
            var gm = GameModeManager.Instance; gm.StartFreePractice(); gm.SetPracticeDistance(10f); gm.SetPracticeMotion(TargetMotion.Static); yield return null;
            var lane = Lane(2); var face = lane.Target.Face;
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var def = pistol.Definition;
            float t = 10f / def.muzzleVelocity;
            float expected = 0.5f * Mathf.Abs(Physics.gravity.y) * def.gravityMultiplier * t * t; // ~0.18 m at 10 m for the pistol
            // aim exactly horizontal at a point above the face centre: the hit must land below the aim point by the drop
            float aimY = face.position.y + 0.1f;
            var origin = new Vector3(face.position.x, aimY, 0f);
            XrTestRig.Aim(pistol, origin, origin + Vector3.forward * 10f);
            HitResult? hit = null; lane.Target.Hit += (_, r) => hit = r;
            Assert.IsTrue(pistol.TryFire());
            yield return XrTestRig.WaitUntil(() => hit.HasValue, 3f, "projectile to reach the 10 m target");
            float drop = aimY - hit.Value.worldPoint.y;
            Assert.Greater(drop, expected * 0.5f, $"visible drop expected (~{expected:0.000} m)");
            Assert.Less(drop, expected * 1.6f + 0.03f, "but consistent with the configured gravity multiplier");
            Assert.AreEqual(10f, hit.Value.distance, 1.0f, "reported travel distance");
        }

        [UnityTest]
        public IEnumerator HitSpawnsImpactEffectsAndTargetMarker()
        {
            var gm = GameModeManager.Instance; gm.StartFreePractice(); gm.SetPracticeDistance(10f); gm.SetPracticeMotion(TargetMotion.Static); yield return null;
            var lane = Lane(1); var face = lane.Target.Face;
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var origin = new Vector3(face.position.x, face.position.y, 0f);
            XrTestRig.Aim(pistol, origin, face.position + Vector3.up * 0.02f);
            int before = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("BulletHoleDecal") && t.gameObject.activeInHierarchy);
            pistol.TryFire();
            yield return XrTestRig.WaitUntil(() => lane.Target.HitsSinceReset > 0, 3f, "hit");
            yield return null;
            int after = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("BulletHoleDecal") && t.gameObject.activeInHierarchy);
            Assert.AreEqual(before + 1, after, "one bullet-hole decal placed");
            Assert.IsTrue(face.GetComponentsInChildren<Transform>().Any(t => t.name.StartsWith("ImpactMarker") && t.gameObject.activeInHierarchy), "impact marker on the face");
            lane.ResetLane(); yield return null;
            Assert.IsFalse(face.GetComponentsInChildren<Transform>().Any(t => t.name.StartsWith("ImpactMarker") && t.gameObject.activeInHierarchy), "markers cleared on reset");
            Assert.AreEqual(before, Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("BulletHoleDecal") && t.gameObject.activeInHierarchy), "decals on the target cleared on reset");
        }

        [UnityTest]
        public IEnumerator ScoringMatchesRingDistance()
        {
            var gm = GameModeManager.Instance; gm.StartFreePractice(); gm.SetPracticeDistance(5f); gm.SetPracticeMotion(TargetMotion.Static); yield return null;
            var lane = Lane(2); var face = lane.Target.Face; var rules = lane.Target.Rules;
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var def = pistol.Definition;
            // Spread is random; aim at a point inside the "Middle" ring far from its edges and accept neighbouring rings for the 0.35 deg cone.
            float t = 5f / def.muzzleVelocity; float drop = 0.5f * Mathf.Abs(Physics.gravity.y) * def.gravityMultiplier * t * t;
            var aimPoint = face.position + face.right * 0.16f + Vector3.up * drop; // 0.16 m right of centre: middle ring (0.12-0.20)
            var origin = new Vector3(face.position.x, face.position.y, 0f);
            XrTestRig.Aim(pistol, origin, aimPoint);
            HitResult? hit = null; lane.Target.Hit += (_, r) => hit = r;
            int scoreBefore = ScoreManager.Instance.Stats.score;
            pistol.TryFire();
            yield return XrTestRig.WaitUntil(() => hit.HasValue, 3f, "hit");
            float r = Vector3.Distance(face.InverseTransformPoint(hit.Value.worldPoint), Vector3.zero);
            Assert.AreEqual(rules.PointsFor(rules.RingIndexFor(r)), hit.Value.points, "points follow the ring radius of the actual hit");
            Assert.That(hit.Value.points, Is.EqualTo(5).Or.EqualTo(8).Or.EqualTo(2), "aimed at the middle ring (neighbours allowed for spread)");
            Assert.AreEqual(scoreBefore + hit.Value.points, ScoreManager.Instance.Stats.score);
        }

        [UnityTest]
        public IEnumerator TargetMotionTypesBehaveAsSpecified()
        {
            var gm = GameModeManager.Instance; gm.StartFreePractice(); gm.SetDifficulty(Difficulty.Medium); gm.SetPracticeDistance(10f);
            var lane = Lane(0); var carrier = lane.Motion.Carrier;
            gm.SetPracticeMotion(TargetMotion.Static); yield return null;
            var start = carrier.localPosition;
            yield return XrTestRig.WaitSeconds(0.5f);
            Assert.AreEqual(start, carrier.localPosition, "static target does not move");

            gm.SetPracticeMotion(TargetMotion.Horizontal); yield return null;
            float maxX = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < 60; i++) { yield return null; var p = carrier.localPosition; maxX = Mathf.Max(maxX, Mathf.Abs(p.x)); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            Assert.Greater(maxX, 0.05f, "horizontal motion moves sideways");
            Assert.LessOrEqual(maxX, gm.Settings.Profile(Difficulty.Medium).lateralRange + 0.01f);
            Assert.AreEqual(10f, minZ, 0.01f); Assert.AreEqual(10f, maxZ, 0.01f);

            gm.SetPracticeMotion(TargetMotion.NearFar); yield return null;
            float maxAbsX = 0f; minZ = float.MaxValue; maxZ = float.MinValue;
            for (float el = 0f; el < 1.2f; el += Time.unscaledDeltaTime) { yield return null; var p = carrier.localPosition; maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(p.x)); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            Assert.Greater(maxZ - minZ, 0.02f, "near/far motion moves in depth (cosine start, so it accelerates from rest)"); Assert.Less(maxAbsX, 0.01f);
            Assert.GreaterOrEqual(minZ, 4f); Assert.LessOrEqual(maxZ, 27.5f);

            gm.SetPracticeMotion(TargetMotion.Random); yield return null;
            var p0 = carrier.localPosition;
            yield return XrTestRig.WaitSeconds(0.6f);
            Assert.Greater(Vector3.Distance(p0, carrier.localPosition), 0.05f, "random motion moves the carrier");

            gm.SetPracticeMotion(TargetMotion.PopUp); yield return null;
            Assert.IsTrue(lane.Motion.IsExposed, "pop-up starts exposed");
            var pivot = lane.Target.transform.Find("PopUpPivot");
            yield return XrTestRig.WaitUntil(() => !lane.Motion.IsExposed, gm.Settings.Profile(Difficulty.Medium).popUpExposure + 1f, "pop-up to hide");
            yield return XrTestRig.WaitSeconds(0.4f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, pivot.localRotation), 30f, "hidden target folds away");
            yield return XrTestRig.WaitUntil(() => lane.Motion.IsExposed, gm.Settings.Profile(Difficulty.Medium).popUpHidden * 1.3f + 1f, "pop-up to show again");

            lane.ResetLane(); yield return null;
            Assert.AreEqual(new Vector3(0f, 0f, 10f), carrier.localPosition, "reset returns to the exact base pose");
        }

        [UnityTest]
        public IEnumerator DifficultyScalesTargetSpeedAndExposure()
        {
            var gm = GameModeManager.Instance; var s = gm.Settings;
            var easy = s.Profile(Difficulty.Easy); var med = s.Profile(Difficulty.Medium); var hard = s.Profile(Difficulty.Hard);
            Assert.Less(easy.moveSpeed, med.moveSpeed); Assert.Less(med.moveSpeed, hard.moveSpeed);
            Assert.Greater(easy.popUpExposure, med.popUpExposure); Assert.Greater(med.popUpExposure, hard.popUpExposure);
            Assert.LessOrEqual(easy.scoreMultiplier, hard.scoreMultiplier);
            gm.StartFreePractice(); gm.SetPracticeDistance(10f);
            float Travel(Difficulty d)
            {
                gm.SetDifficulty(d); gm.SetPracticeMotion(TargetMotion.Static); gm.SetPracticeMotion(TargetMotion.NearFar);
                return 0f;
            }
            Travel(Difficulty.Easy); yield return null;
            var carrier = Lane(3).Motion.Carrier; float dist = 0f; var last = carrier.localPosition;
            for (float el = 0f; el < 0.6f; el += Time.unscaledDeltaTime) { yield return null; dist += Vector3.Distance(last, carrier.localPosition); last = carrier.localPosition; }
            float easyDist = dist;
            Travel(Difficulty.Hard); yield return null;
            dist = 0f; last = carrier.localPosition;
            for (float el = 0f; el < 0.6f; el += Time.unscaledDeltaTime) { yield return null; dist += Vector3.Distance(last, carrier.localPosition); last = carrier.localPosition; }
            Assert.Greater(dist, easyDist * 1.5f, $"hard targets travel further in the same time (easy {easyDist:0.000} m, hard {dist:0.000} m)");
        }

        [UnityTest]
        public IEnumerator DistanceIsClampedToPresets()
        {
            var gm = GameModeManager.Instance; gm.StartFreePractice();
            gm.SetPracticeDistance(12f); yield return null;
            Assert.AreEqual(10f, gm.CurrentDistance, "12 m snaps to the nearest preset (10)");
            Assert.AreEqual(10f, Lane(0).CurrentDistance);
            gm.SetPracticeDistance(1000f); yield return null;
            Assert.AreEqual(25f, gm.CurrentDistance);
            gm.SetPracticeDistanceIndex(-5); yield return null; Assert.AreEqual(5f, gm.CurrentDistance, "index clamped low");
            gm.SetPracticeDistanceIndex(99); yield return null; Assert.AreEqual(25f, gm.CurrentDistance, "index clamped high");
            Assert.AreEqual(25f, Lane(4).Motion.Carrier.localPosition.z, 0.01f);
        }

        [UnityTest]
        public IEnumerator ProjectileNeverHitsItsOwnWeaponOrOtherProjectiles()
        {
            var rifle = XrTestRig.Find(WeaponClass.Rifle);
            int lp = LayerMask.NameToLayer("Projectile"), lw = LayerMask.NameToLayer("Weapon");
            Assert.GreaterOrEqual(lp, 0); Assert.GreaterOrEqual(lw, 0);
            var mask = ProjectilePool.Instance.HitMask.value;
            Assert.AreEqual(0, mask & (1 << lp), "projectiles ignore the Projectile layer");
            Assert.AreEqual(0, mask & (1 << lw), "projectiles ignore the Weapon layer");
            // fire with the muzzle inside another weapon's collider: the shot must pass through weapons
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            XrTestRig.Aim(rifle, pistol.transform.position + Vector3.back * 0.3f, pistol.transform.position + Vector3.forward * 20f);
            int shots = ScoreManager.Instance.Stats.shotsFired;
            rifle.TryFire(); yield return null;
            var proj = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsActive);
            Assert.IsNotNull(proj, "projectile in flight (did not die inside the weapon)");
            yield return XrTestRig.WaitSeconds(0.1f);
            Assert.Greater(proj.transform.position.z, pistol.transform.position.z + 0.5f, "passed through the weapon on the counter");
            Assert.AreEqual(shots + 1, ScoreManager.Instance.Stats.shotsFired);
        }
    }
}
