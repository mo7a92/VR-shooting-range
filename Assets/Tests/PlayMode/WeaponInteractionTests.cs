using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Scoring;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// Weapon state machine driven through XRI events with scripted interactors (mocked controllers, real weapon logic):
    /// pickup/release/reacquire, trigger semantics per fire mode, ammo, dry fire, reload transitions, fire-mode toggle,
    /// socket return and "no stuck state" after many cycles.
    /// </summary>
    public class WeaponInteractionTests
    {
        [UnitySetUp] public IEnumerator Setup() => XrTestRig.LoadScene();

        static readonly Vector3 HandPos = new Vector3(0f, 1.4f, -1f);

        [UnityTest]
        public IEnumerator PickupReleaseAndReacquireKeepsSingleCleanState()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            Assert.IsFalse(pistol.IsHeld, "weapon should start in its socket, not in a hand");
            Assert.IsTrue(pistol.Grab.isSelected, "weapon should be socketed at start");
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            int gripEvents = 0; pistol.GripChanged += _ => gripEvents++;
            yield return XrTestRig.Grab(hand, pistol);
            Assert.IsTrue(pistol.IsHeld); Assert.AreEqual(1, pistol.HandCount); Assert.AreSame(hand, pistol.PrimaryHandInteractor);
            Assert.AreEqual(1, pistol.Grab.interactorsSelecting.Count, "socket must release the weapon when a hand takes it");
            XrTestRig.Release(hand, pistol);
            yield return null;
            Assert.IsFalse(pistol.IsHeld); Assert.AreEqual(0, pistol.HandCount);
            yield return XrTestRig.Grab(hand, pistol);
            Assert.IsTrue(pistol.IsHeld, "reacquire after release");
            Assert.GreaterOrEqual(gripEvents, 3, "GripChanged on grab, release, grab");
            Assert.AreEqual(3, XrTestRig.AllWeapons().Length, "no weapon was duplicated");
        }

        [UnityTest]
        public IEnumerator HandSwapTransfersPrimaryHand()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var right = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            var left = XrTestRig.MakeHand("L", HandPos + Vector3.left * 0.3f, Quaternion.identity, InteractorHandedness.Left);
            yield return XrTestRig.Grab(right, pistol);
            yield return XrTestRig.Grab(left, pistol); // pistol is single-select: the left hand takes over
            Assert.AreEqual(1, pistol.HandCount, "pistol is one-handed");
            Assert.AreSame(left, pistol.PrimaryHandInteractor, "the newest hand owns a single-select weapon");
            Assert.IsFalse(right.hasSelection);
        }

        [UnityTest]
        public IEnumerator SemiAutoFiresOncePerTriggerPullAndConsumesAmmo()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, pistol);
            int fired = 0; pistol.Fired += _ => fired++;
            int rounds = pistol.Rounds;
            XrTestRig.PullTrigger(hand, pistol);
            for (int i = 0; i < 20; i++) yield return null; // holding the trigger on a semi-auto must not fire again
            Assert.AreEqual(1, fired, "semi-auto: exactly one shot while the trigger stays held");
            Assert.AreEqual(rounds - 1, pistol.Rounds);
            XrTestRig.ReleaseTrigger(hand, pistol);
            yield return XrTestRig.WaitSeconds(pistol.Definition.SecondsBetweenShots + 0.05f);
            XrTestRig.PullTrigger(hand, pistol); yield return null;
            Assert.AreEqual(2, fired, "second pull fires again");
            Assert.AreEqual(rounds - 2, pistol.Rounds);
        }

        [UnityTest]
        public IEnumerator FireRateIsRespectedOnRapidPulls()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, pistol);
            int fired = 0; pistol.Fired += _ => fired++;
            for (int i = 0; i < 10; i++) { XrTestRig.PullTrigger(hand, pistol); XrTestRig.ReleaseTrigger(hand, pistol); }
            Assert.AreEqual(1, fired, "10 pulls in one frame must only fire once (rate limit)");
        }

        [UnityTest]
        public IEnumerator AutomaticFireContinuesWhileHeldAndStopsOnRelease()
        {
            var smg = XrTestRig.Find(WeaponClass.SubmachineGun);
            Assert.AreEqual(FireMode.Auto, smg.CurrentFireMode, "SMG defaults to automatic");
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, smg);
            int fired = 0; smg.Fired += _ => fired++;
            XrTestRig.PullTrigger(hand, smg);
            yield return XrTestRig.WaitSeconds(0.5f);
            XrTestRig.ReleaseTrigger(hand, smg);
            int atRelease = fired;
            float expected = 0.5f / smg.Definition.SecondsBetweenShots;
            Assert.That(fired, Is.InRange(Mathf.FloorToInt(expected * 0.5f), Mathf.CeilToInt(expected * 1.5f) + 1), $"auto fire rate (~{expected:0} shots in 0.5 s)");
            Assert.Greater(fired, 2);
            yield return XrTestRig.WaitSeconds(0.3f);
            Assert.AreEqual(atRelease, fired, "no shots after the trigger is released");
        }

        [UnityTest]
        public IEnumerator DroppingTheWeaponStopsAutomaticFire()
        {
            var smg = XrTestRig.Find(WeaponClass.SubmachineGun);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, smg);
            int fired = 0; smg.Fired += _ => fired++;
            XrTestRig.PullTrigger(hand, smg);
            yield return XrTestRig.WaitSeconds(0.2f);
            XrTestRig.Release(hand, smg); // dropped while the trigger is still "held"
            int atDrop = fired; yield return XrTestRig.WaitSeconds(0.3f);
            Assert.AreEqual(atDrop, fired, "a dropped weapon must not keep firing (stuck trigger state)");
            yield return XrTestRig.Grab(hand, smg);
            yield return XrTestRig.WaitSeconds(0.2f);
            Assert.AreEqual(atDrop, fired, "re-grabbing must not resume firing without a new trigger pull");
        }

        [UnityTest]
        public IEnumerator EmptyMagazineDryFiresAndReloadRestoresAmmo()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, pistol);
            int dry = 0, fired = 0; pistol.DryFired += _ => dry++; pistol.Fired += _ => fired++;
            var reloadStates = new System.Collections.Generic.List<bool>(); pistol.ReloadStateChanged += (_, r) => reloadStates.Add(r);
            int mag = pistol.MagazineSize;
            for (int i = 0; i < mag; i++) { Assert.IsTrue(pistol.TryFire(), $"shot {i} should fire"); yield return XrTestRig.WaitSeconds(pistol.Definition.SecondsBetweenShots + 0.02f); }
            Assert.AreEqual(0, pistol.Rounds); Assert.AreEqual(mag, fired);
            int projectilesBefore = ScoreManager.Instance.Stats.shotsFired;
            Assert.IsFalse(pistol.TryFire(), "empty magazine must not fire");
            Assert.AreEqual(1, dry, "dry-fire event");
            Assert.AreEqual(projectilesBefore, ScoreManager.Instance.Stats.shotsFired, "dry fire is not a shot");
            pistol.StartReload();
            yield return null;
            Assert.IsTrue(pistol.IsReloading);
            Assert.IsFalse(pistol.TryFire(), "cannot fire while reloading");
            yield return XrTestRig.WaitSeconds(pistol.Definition.reloadSeconds + 0.4f);
            Assert.IsFalse(pistol.IsReloading);
            Assert.AreEqual(mag, pistol.Rounds, "magazine refilled");
            CollectionAssert.AreEqual(new[] { true, false }, reloadStates, "reload state transitions exactly once in and out");
            Assert.IsTrue(pistol.TryFire(), "fires again after reload");
        }

        [UnityTest]
        public IEnumerator ReloadIsIgnoredWhenFullOrAlreadyReloading()
        {
            var rifle = XrTestRig.Find(WeaponClass.Rifle);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, rifle);
            int transitions = 0; rifle.ReloadStateChanged += (_, __) => transitions++;
            rifle.StartReload(); yield return null;
            Assert.IsFalse(rifle.IsReloading, "full magazine: reload request ignored");
            Assert.AreEqual(0, transitions);
            rifle.TryFire(); yield return null;
            rifle.StartReload(); rifle.StartReload(); rifle.StartReload(); yield return null;
            Assert.IsTrue(rifle.IsReloading);
            yield return XrTestRig.WaitSeconds(rifle.Definition.reloadSeconds + 0.4f);
            Assert.AreEqual(2, transitions, "repeated requests during a reload do not restart it");
            Assert.AreEqual(rifle.MagazineSize, rifle.Rounds);
        }

        [UnityTest]
        public IEnumerator ReleasingDuringReloadCompletesWithoutErrorsAndRefillCancelsReload()
        {
            var smg = XrTestRig.Find(WeaponClass.SubmachineGun);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, smg);
            smg.TryFire(); yield return null;
            smg.StartReload(); yield return null;
            XrTestRig.Release(hand, smg);
            yield return XrTestRig.WaitSeconds(smg.Definition.reloadSeconds + 0.4f);
            Assert.IsFalse(smg.IsReloading); Assert.AreEqual(smg.MagazineSize, smg.Rounds, "reload finished even though the weapon was dropped");
            // RefillInstant (mode restart) cancels a running reload immediately
            yield return XrTestRig.Grab(hand, smg);
            smg.TryFire(); yield return null; smg.StartReload(); yield return null;
            Assert.IsTrue(smg.IsReloading);
            smg.RefillInstant();
            Assert.IsFalse(smg.IsReloading); Assert.AreEqual(smg.MagazineSize, smg.Rounds);
            yield return XrTestRig.WaitSeconds(smg.Definition.reloadSeconds + 0.3f);
            Assert.IsFalse(smg.IsReloading, "cancelled coroutine must not flip state later");
        }

        [UnityTest]
        public IEnumerator FireModeToggleRespectsSupportedModes()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol); var rifle = XrTestRig.Find(WeaponClass.Rifle); var smg = XrTestRig.Find(WeaponClass.SubmachineGun);
            int pistolEvents = 0; pistol.FireModeChanged += (_, __) => pistolEvents++;
            pistol.ToggleFireMode();
            Assert.AreEqual(FireMode.Semi, pistol.CurrentFireMode, "pistol is semi-auto only"); Assert.AreEqual(0, pistolEvents);
            FireMode last = FireMode.None; rifle.FireModeChanged += (_, m) => last = m;
            rifle.ToggleFireMode(); Assert.AreEqual(FireMode.Auto, rifle.CurrentFireMode); Assert.AreEqual(FireMode.Auto, last);
            rifle.ToggleFireMode(); Assert.AreEqual(FireMode.Semi, rifle.CurrentFireMode);
            smg.ToggleFireMode(); Assert.AreEqual(FireMode.Semi, smg.CurrentFireMode); smg.ToggleFireMode(); Assert.AreEqual(FireMode.Auto, smg.CurrentFireMode);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AmmoCounterEventsTrackEveryChange()
        {
            var rifle = XrTestRig.Find(WeaponClass.Rifle);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            int lastRounds = -1, lastSize = -1, events = 0;
            rifle.AmmoChanged += (_, r, s) => { lastRounds = r; lastSize = s; events++; };
            yield return XrTestRig.Grab(hand, rifle);
            Assert.AreEqual(rifle.MagazineSize, lastRounds, "pickup announces the current ammo");
            rifle.TryFire(); yield return null;
            Assert.AreEqual(rifle.MagazineSize - 1, lastRounds); Assert.AreEqual(rifle.MagazineSize, lastSize);
            rifle.RefillInstant();
            Assert.AreEqual(rifle.MagazineSize, lastRounds);
            Assert.GreaterOrEqual(events, 3);
        }

        [UnityTest]
        public IEnumerator WeaponReturnsToSocketAndWhenFallingThroughTheFloor()
        {
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            var home = pistol.GetComponent<WeaponHome>();
            Assert.IsNotNull(home); Assert.IsNotNull(home.HomeSocket, "weapon knows its socket");
            var socketPos = home.HomeSocket.attachTransform.position;
            var hand = XrTestRig.MakeHand("R", HandPos + new Vector3(1.5f, 0f, 0f), Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, pistol);
            Assert.Greater(Vector3.Distance(pistol.transform.position, socketPos), 0.5f, "weapon left the socket");
            XrTestRig.Release(hand, pistol); yield return null;
            home.ReturnHome(); yield return null; yield return null;
            Assert.Less(Vector3.Distance(pistol.transform.position, socketPos), 0.05f, "ReturnHome puts the weapon back");
            Assert.IsTrue(home.HomeSocket.hasSelection, "socket holds the weapon again");
            // falling out of the world: the Update() guard returns it without waiting for the timer
            yield return XrTestRig.Grab(hand, pistol);
            XrTestRig.Release(hand, pistol); yield return null;
            pistol.transform.position = new Vector3(0f, -5f, 0f);
            yield return null; yield return null; yield return null;
            Assert.Less(Vector3.Distance(pistol.transform.position, socketPos), 0.05f, "weapon below the floor is recovered");
        }

        [UnityTest]
        public IEnumerator ManyGrabReleaseCyclesLeaveNoStuckOrDuplicateState()
        {
            var weapons = XrTestRig.AllWeapons();
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            for (int cycle = 0; cycle < 12; cycle++)
            {
                var w = weapons[cycle % weapons.Length];
                yield return XrTestRig.Grab(hand, w, 1);
                XrTestRig.PullTrigger(hand, w); yield return null; XrTestRig.ReleaseTrigger(hand, w);
                XrTestRig.Release(hand, w); yield return null;
                Assert.IsFalse(w.IsHeld); Assert.AreEqual(0, w.HandCount);
            }
            Assert.AreEqual(3, XrTestRig.AllWeapons().Length, "weapon count unchanged");
            Assert.IsFalse(hand.hasSelection, "hand ends empty");
            foreach (var w in weapons) { Assert.IsFalse(w.IsReloading); Assert.Greater(w.Rounds, 0); }
            yield return XrTestRig.WaitSeconds(0.5f);
            Assert.AreEqual(3, XrTestRig.AllWeapons().Length);
        }

        [UnityTest]
        public IEnumerator FiringSendsShotsToScoreManagerAndSpawnsPooledProjectiles()
        {
            var rifle = XrTestRig.Find(WeaponClass.Rifle);
            var hand = XrTestRig.MakeHand("R", HandPos, Quaternion.identity, InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, rifle);
            int shots = ScoreManager.Instance.Stats.shotsFired;
            Assert.IsTrue(rifle.TryFire());
            yield return null;
            Assert.AreEqual(shots + 1, ScoreManager.Instance.Stats.shotsFired);
            Assert.GreaterOrEqual(XrTestRig.ActiveProjectiles(), 1, "a projectile is in flight");
            Assert.IsNotNull(ProjectilePool.Instance);
        }
    }
}
