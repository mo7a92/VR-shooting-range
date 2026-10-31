using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Hands;
using VRRange.Modes;
using VRRange.Scoring;
using VRRange.UI;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>
    /// The complete session as one scripted run (mocked controllers via the rig's own interactors; everything else real):
    /// launch -> range -> mode -> pistol fire/reload -> rifle two-handed -> moving targets -> SMG -> finish -> results -> restart,
    /// then cross-system cleanup checks (pools, sockets, hands, state).
    /// </summary>
    public class FullGameplaySequenceTests
    {
        float m_TimeScale;
        [UnitySetUp] public IEnumerator Setup() { m_TimeScale = Time.timeScale; PlayerPrefs.DeleteKey($"VRRange.Best.{GameMode.MovingTargetChallenge}.{Difficulty.Medium}"); yield return XrTestRig.LoadScene(); }
        [UnityTearDown] public IEnumerator TearDown() { Time.timeScale = m_TimeScale; yield return null; }

        [UnityTest]
        public IEnumerator CompleteSessionFromLaunchToRestart()
        {
            // 1. Launch: managers, rig, teleport area, UI
            var gm = GameModeManager.Instance; var ui = Object.FindFirstObjectByType<RangeUI>(); var origin = Object.FindFirstObjectByType<XROrigin>();
            Assert.IsNotNull(gm); Assert.IsNotNull(ScoreManager.Instance); Assert.IsNotNull(ProjectilePool.Instance); Assert.IsNotNull(ui); Assert.IsNotNull(origin);
            Assert.IsNotNull(Object.FindFirstObjectByType<TeleportationArea>(), "teleport area exists");
            Assert.IsNotNull(Object.FindFirstObjectByType<RangeRuntimeInfo>());
            // 2. Enter the range: the rig stands behind the firing line facing downrange, at floor level
            Assert.Less(origin.transform.position.z, 0f); Assert.AreEqual(0f, origin.transform.position.y, 0.01f);
            Assert.AreEqual(XROrigin.TrackingOriginMode.Floor, origin.RequestedTrackingOriginMode, "real player height is used");
            var hands = Object.FindObjectsByType<HandPresence>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var h in hands) for (var t = h.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            yield return null;
            var right = hands.First(h => !h.IsLeft); var left = hands.First(h => h.IsLeft);
            var rc = right.transform.parent; var lc = left.transform.parent;
            rc.SetPositionAndRotation(new Vector3(0.2f, 1.35f, -1f), Quaternion.identity); lc.SetPositionAndRotation(new Vector3(-0.2f, 1.35f, -1f), Quaternion.identity);
            var rInteractor = rc.GetComponentInChildren<XRBaseInputInteractor>(true); var lInteractor = lc.GetComponentInChildren<XRBaseInputInteractor>(true);
            var manager = XrTestRig.Manager;

            // 3. Select a game mode from the VR menu
            ui.difficultyButtons[1].onClick.Invoke();
            ui.freePracticeButton.onClick.Invoke(); yield return null;
            Assert.AreEqual(GameMode.FreePractice, gm.CurrentMode); Assert.IsFalse(ui.mainMenuPanel.activeSelf);

            // 4. Pick up the pistol with the right hand
            var pistol = XrTestRig.Find(WeaponClass.Pistol); var rifle = XrTestRig.Find(WeaponClass.Rifle); var smg = XrTestRig.Find(WeaponClass.SubmachineGun);
            manager.SelectEnter((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)pistol.Grab);
            yield return XrTestRig.WaitSeconds(0.4f);
            Assert.IsTrue(pistol.IsHeld); Assert.AreSame(pistol, right.HeldWeapon, "hand poses onto the pistol");
            StringAssert.Contains("Pistol", ui.ammoLabel.text);

            // 5. Fire and reload
            int shotsBefore = ScoreManager.Instance.Stats.shotsFired;
            for (int i = 0; i < 3; i++) { XrTestRig.PullTrigger(rInteractor, pistol); XrTestRig.ReleaseTrigger(rInteractor, pistol); yield return XrTestRig.WaitSeconds(pistol.Definition.SecondsBetweenShots + 0.02f); }
            Assert.AreEqual(shotsBefore + 3, ScoreManager.Instance.Stats.shotsFired, "three shots counted");
            Assert.AreEqual(pistol.MagazineSize - 3, pistol.Rounds);
            pistol.StartReload(); yield return null; Assert.IsTrue(pistol.IsReloading);
            yield return XrTestRig.WaitSeconds(pistol.Definition.reloadSeconds + 0.4f);
            Assert.AreEqual(pistol.MagazineSize, pistol.Rounds, "reloaded");

            // 6. Switch to the rifle (put the pistol back first)
            manager.SelectExit((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)pistol.Grab); yield return null;
            pistol.GetComponent<WeaponHome>().ReturnHome(); yield return null;
            manager.SelectEnter((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)rifle.Grab);
            yield return XrTestRig.WaitSeconds(0.4f);
            Assert.IsTrue(rifle.IsHeld); Assert.AreSame(rifle, right.HeldWeapon);

            // 7. Use both hands
            lc.position = rc.position + new Vector3(-0.06f, 0.05f, 0.32f); yield return null;
            manager.SelectEnter((IXRSelectInteractor)lInteractor, (IXRSelectInteractable)rifle.Grab);
            yield return XrTestRig.WaitSeconds(0.6f);
            Assert.IsTrue(rifle.IsTwoHanded); Assert.AreEqual(2, rifle.HandCount); Assert.AreSame(rifle, left.HeldWeapon);
            Assert.Greater(rifle.Handling.TwoHandBlend, 0.9f);

            // 8. Fire at moving targets (Moving Target Challenge, Medium) in automatic mode
            ui.movingButton.onClick.Invoke();
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Running, 6f, "countdown");
            Assert.IsTrue(gm.Lanes.All(l => l.Motion.Motion != TargetMotion.Static), "targets are moving");
            rifle.ToggleFireMode(); Assert.AreEqual(FireMode.Auto, rifle.CurrentFireMode);
            int rifleShots = 0; rifle.Fired += _ => rifleShots++;
            XrTestRig.PullTrigger(rInteractor, rifle); yield return XrTestRig.WaitSeconds(0.6f); XrTestRig.ReleaseTrigger(rInteractor, rifle);
            Assert.Greater(rifleShots, 3, "automatic burst");
            Assert.Greater(Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Count(r => r.name.StartsWith("ShellCasing") && r.gameObject.activeInHierarchy), 0, "casings ejected");

            // 9. Switch to the SMG (both hands release, SMG in the right hand)
            manager.SelectExit((IXRSelectInteractor)lInteractor, (IXRSelectInteractable)rifle.Grab); yield return null;
            manager.SelectExit((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)rifle.Grab); yield return null;
            rifle.GetComponent<WeaponHome>().ReturnHome(); yield return null;
            Assert.IsNull(right.HeldWeapon); Assert.IsNull(left.HeldWeapon);
            manager.SelectEnter((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)smg.Grab);
            yield return XrTestRig.WaitSeconds(0.4f);
            Assert.IsTrue(smg.IsHeld);
            // score a guaranteed hit so the results have content (aim helper, scripted)
            var lane = gm.Lanes[2];
            manager.SelectExit((IXRSelectInteractor)rInteractor, (IXRSelectInteractable)smg.Grab); yield return null;
            int hitsBefore = ScoreManager.Instance.Stats.hits;
            yield return XrTestRig.FireUntilHit(smg, lane.Target, 10, 0.8f, requireScore: true); // moving target: edge hits on the frame score 0, so insist on a ring hit
            Assert.Greater(ScoreManager.Instance.Stats.hits, hitsBefore, "SMG hit on a moving target scored");

            // 10. Complete the challenge (fast-forward the 60 s timer)
            Time.timeScale = 20f;
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Finished, 20f, "challenge timer");
            Time.timeScale = 1f;

            // 11. View the final score
            Assert.IsTrue(ui.resultsPanel.activeSelf, "results shown");
            StringAssert.Contains("Moving Target Challenge complete", ui.resultsTitle.text);
            Assert.Greater(gm.LastResult.stats.score, 0); Assert.IsTrue(gm.LastResult.isNewPersonalBest);
            Assert.AreEqual(gm.LastResult.stats.score, GameModeManager.PersonalBest(GameMode.MovingTargetChallenge, Difficulty.Medium), "best persisted");

            // 12. Restart
            ui.restartButton.onClick.Invoke(); yield return null;
            Assert.AreEqual(ModeState.Countdown, gm.State); Assert.AreEqual(0, ScoreManager.Instance.Stats.score); Assert.AreEqual(0, ScoreManager.Instance.Stats.shotsFired);
            Assert.IsTrue(XrTestRig.AllWeapons().All(w => w.Rounds == w.MagazineSize && !w.IsReloading), "all weapons refilled");
            Assert.IsTrue(gm.Lanes.All(l => l.Target.HitsSinceReset == 0), "targets reset");
            Assert.IsFalse(ui.resultsPanel.activeSelf);

            // cleanup and cross-system state
            smg.GetComponent<WeaponHome>().ReturnHome(); yield return null;
            yield return XrTestRig.WaitUntil(() => XrTestRig.ActiveProjectiles() == 0, 5f, "all projectiles recycled");
            yield return XrTestRig.WaitUntil(() => Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Count(r => r.name.StartsWith("ShellCasing") && r.gameObject.activeInHierarchy) == 0, 6f, "all casings recycled");
            Assert.AreEqual(3, XrTestRig.AllWeapons().Length); Assert.AreEqual(2, Object.FindObjectsByType<HandPresence>(FindObjectsSortMode.None).Length);
            foreach (var w in XrTestRig.AllWeapons())
            {
                var home = w.GetComponent<WeaponHome>();
                Assert.IsTrue(home.HomeSocket.hasSelection, $"{w.DisplayName} back in its socket");
                Assert.Less(Vector3.Distance(w.transform.position, home.HomeSocket.transform.position), 0.05f, $"{w.DisplayName} rests on its socket");
                Assert.IsFalse(w.IsHeld);
            }
            Assert.IsNull(right.HeldWeapon); Assert.IsNull(left.HeldWeapon);
            Assert.IsFalse(rInteractor.hasSelection); Assert.IsFalse(lInteractor.hasSelection);
            Assert.AreEqual(1, Object.FindObjectsByType<GameModeManager>(FindObjectsSortMode.None).Length); Assert.AreEqual(1, Object.FindObjectsByType<ScoreManager>(FindObjectsSortMode.None).Length);
        }

    }
}
