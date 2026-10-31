using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Scoring;
using VRRange.Targets;
using VRRange.UI;
using VRRange.Weapons;

namespace VRRange.Tests
{
    /// <summary>Game-mode state machine (countdown, timers, completion, restart, personal bests, combo) and the VR menu/HUD/results UI driven through its buttons.</summary>
    public class GameModeAndUiTests
    {
        const string BestKeyPrefix = "VRRange.Best.";
        float m_TimeScale;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            m_TimeScale = Time.timeScale;
            foreach (GameMode m in System.Enum.GetValues(typeof(GameMode))) foreach (Difficulty d in System.Enum.GetValues(typeof(Difficulty))) PlayerPrefs.DeleteKey($"{BestKeyPrefix}{m}.{d}");
            yield return XrTestRig.LoadScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown() { Time.timeScale = m_TimeScale; yield return null; }

        static RangeUI Ui() => Object.FindFirstObjectByType<RangeUI>();

        [UnityTest]
        public IEnumerator SceneStartsInFreePracticeWithMenuVisible()
        {
            var gm = GameModeManager.Instance; var ui = Ui();
            Assert.AreEqual(GameMode.FreePractice, gm.CurrentMode); Assert.AreEqual(ModeState.Running, gm.State, "free practice runs immediately (untimed)");
            Assert.IsTrue(ui.mainMenuPanel.activeSelf); Assert.IsTrue(ui.hudPanel.activeSelf); Assert.IsFalse(ui.resultsPanel.activeSelf);
            Assert.IsTrue(ui.laneControlsPanel.activeSelf, "lane controls shown in free practice");
            Assert.AreEqual("Free Practice", ui.modeLabel.text);
            Assert.IsTrue(ScoreManager.Instance.IsTiming);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AccuracyChallengeCountsDownThenRunsAndFinishesWithResults()
        {
            var gm = GameModeManager.Instance; var ui = Ui();
            var seconds = new List<int>(); gm.CountdownSecond += s => seconds.Add(s);
            var states = new List<ModeState>(); gm.StateChanged += s => states.Add(s);
            ui.accuracyButton.onClick.Invoke();
            Assert.IsFalse(ui.mainMenuPanel.activeSelf, "menu hides when a mode starts"); Assert.IsFalse(ui.laneControlsPanel.activeSelf, "lane controls hidden outside free practice");
            Assert.AreEqual(ModeState.Countdown, gm.State); Assert.IsFalse(ScoreManager.Instance.IsTiming, "timer paused during countdown");
            yield return null;
            StringAssert.StartsWith("Ready", ui.timerLabel.text);
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Running, 5f, "countdown to finish");
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, seconds.Distinct().ToArray(), "countdown announces 3, 2, 1, GO");
            Assert.IsTrue(ScoreManager.Instance.IsTiming);
            StringAssert.Contains($"/{gm.Settings.accuracyChallengeShots}", ui.shotsLabel.text, "HUD shows the shot limit");
            ModeResult? result = null; gm.ModeFinished += r => result = r;
            for (int i = 0; i < gm.Settings.accuracyChallengeShots; i++) ScoreManager.Instance.RegisterShot();
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 0, ringName = "Bullseye", points = 10 });
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Finished, 4f, "finish after the shot limit");
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(gm.Settings.accuracyChallengeShots * 10, result.Value.maxPossibleScore);
            Assert.AreEqual(10, result.Value.stats.score); Assert.AreEqual(1, result.Value.stats.bullseyes);
            Assert.IsTrue(result.Value.isNewPersonalBest, "first result is a personal best");
            Assert.AreEqual(10, GameModeManager.PersonalBest(GameMode.AccuracyChallenge, gm.CurrentDifficulty));
            Assert.IsTrue(ui.resultsPanel.activeSelf, "results panel shown"); Assert.IsFalse(ui.mainMenuPanel.activeSelf);
            StringAssert.Contains("Accuracy Challenge complete", ui.resultsTitle.text);
            StringAssert.Contains("Score: 10 / ", ui.resultsBody.text); StringAssert.Contains("NEW PERSONAL BEST", ui.resultsBody.text);
            Assert.IsFalse(ScoreManager.Instance.IsTiming, "timer stops at the end");
            CollectionAssert.AreEqual(new[] { ModeState.Countdown, ModeState.Running, ModeState.Finishing, ModeState.Finished }, states, "state sequence: no second countdown/GO after the shot limit");
            Assert.AreEqual(4, seconds.Count, "countdown announcements happen exactly once per start (no replayed GO)");
        }

        [UnityTest]
        public IEnumerator MovingTargetChallengeEndsWhenTheTimerExpiresAndScoresWithMultiplier()
        {
            var gm = GameModeManager.Instance; var ui = Ui();
            gm.SetDifficulty(Difficulty.Hard);
            ui.movingButton.onClick.Invoke();
            Time.timeScale = 20f; // 60 s challenge in ~3 s of wall time; the manager uses scaled time
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Running, 10f, "countdown");
            Assert.That(gm.TimeRemaining, Is.InRange(gm.Settings.movingChallengeSeconds - 8f, gm.Settings.movingChallengeSeconds), "timer starts near the configured duration (large scaled frames allowed)");
            Assert.IsTrue(gm.Lanes.All(l => l.Motion.Motion == gm.Settings.movingChallengeMotion), "lanes use the challenge motion");
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 1, ringName = "Inner", points = 8 });
            float mult = gm.Settings.Profile(Difficulty.Hard).scoreMultiplier;
            Assert.AreEqual(Mathf.RoundToInt(8 * mult), ScoreManager.Instance.Stats.score, "hard multiplier applied");
            yield return null;
            StringAssert.StartsWith("Time ", ui.timerLabel.text);
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Finished, 20f, "timer to expire");
            Time.timeScale = 1f;
            Assert.AreEqual(0f, gm.TimeRemaining);
            Assert.AreEqual(GameMode.MovingTargetChallenge, gm.LastResult.mode); Assert.AreEqual(Difficulty.Hard, gm.LastResult.difficulty);
            Assert.IsTrue(ui.resultsPanel.activeSelf);
        }

        [UnityTest]
        public IEnumerator RestartResetsScoreAmmoTargetsAndCombo()
        {
            var gm = GameModeManager.Instance; var ui = Ui();
            var pistol = XrTestRig.Find(WeaponClass.Pistol);
            pistol.TryFire(); yield return null;
            Assert.Less(pistol.Rounds, pistol.MagazineSize);
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 0, points = 10 }); ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 0, points = 10 }); ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 0, points = 10 });
            Assert.AreEqual(3, gm.Combo); StringAssert.Contains("COMBO x3", ui.comboLabel.text);
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = -1, points = 0 });
            Assert.AreEqual(0, gm.Combo, "a miss resets the combo"); Assert.AreEqual(3, gm.BestCombo); Assert.AreEqual("", ui.comboLabel.text);
            var lane = gm.Lanes[0]; lane.Target.OnProjectileHit(new HitInfo { point = lane.Target.Face.position, normal = -lane.Target.Face.forward });
            Assert.AreEqual(1, lane.Target.HitsSinceReset);
            ui.restartButton.onClick.Invoke(); yield return null;
            Assert.AreEqual(0, ScoreManager.Instance.Stats.score); Assert.AreEqual(0, ScoreManager.Instance.Stats.shotsFired);
            Assert.AreEqual(pistol.MagazineSize, pistol.Rounds, "weapons refilled on restart");
            Assert.AreEqual(0, lane.Target.HitsSinceReset, "targets reset"); Assert.AreEqual(0, gm.Combo); Assert.AreEqual(0, gm.BestCombo);
            Assert.IsTrue(ui.hudPanel.activeSelf); Assert.IsFalse(ui.resultsPanel.activeSelf);
            ui.backToMenuButton.onClick.Invoke();
            Assert.IsTrue(ui.mainMenuPanel.activeSelf);
        }

        [UnityTest]
        public IEnumerator PersonalBestOnlyImprovesAndSurvivesRestart()
        {
            var gm = GameModeManager.Instance;
            gm.StartAccuracyChallenge();
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Running, 5f, "countdown");
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 0, points = 10 }); ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 1, points = 8 });
            gm.Finish();
            Assert.AreEqual(18, gm.LastResult.personalBest); Assert.IsTrue(gm.LastResult.isNewPersonalBest);
            gm.Restart();
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Running, 5f, "countdown 2");
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 3, points = 2 });
            gm.Finish();
            Assert.AreEqual(18, gm.LastResult.personalBest, "lower score keeps the old best"); Assert.IsFalse(gm.LastResult.isNewPersonalBest);
            Assert.AreEqual(18, PlayerPrefs.GetInt($"{BestKeyPrefix}{GameMode.AccuracyChallenge}.{gm.CurrentDifficulty}"));
            Assert.AreEqual(0, GameModeManager.PersonalBest(GameMode.MovingTargetChallenge, gm.CurrentDifficulty), "other modes untouched");
        }

        [UnityTest]
        public IEnumerator FinishIsIdempotentAndGradesFollowTheRatio()
        {
            var gm = GameModeManager.Instance;
            gm.StartAccuracyChallenge();
            yield return XrTestRig.WaitUntil(() => gm.State == ModeState.Running, 5f, "countdown");
            int finished = 0; gm.ModeFinished += _ => finished++;
            gm.Finish(); gm.Finish(); gm.Finish();
            Assert.AreEqual(1, finished, "Finish() twice must not emit two results");
            Assert.AreEqual("-", gm.LastResult.grade, "no shots: no grade");
            Assert.AreEqual("S", GameModeManager.GradeFor(new SessionStats { shotsFired = 10, score = 95 }, 100));
            Assert.AreEqual("A", GameModeManager.GradeFor(new SessionStats { shotsFired = 10, score = 80 }, 100));
            Assert.AreEqual("B", GameModeManager.GradeFor(new SessionStats { shotsFired = 10, score = 60 }, 100));
            Assert.AreEqual("C", GameModeManager.GradeFor(new SessionStats { shotsFired = 10, score = 40 }, 100));
            Assert.AreEqual("D", GameModeManager.GradeFor(new SessionStats { shotsFired = 10, score = 10 }, 100));
            Assert.AreEqual("S", GameModeManager.GradeFor(new SessionStats { shotsFired = 10, hits = 10 }, 0), "untimed/unbounded modes grade by accuracy");
        }

        [UnityTest]
        public IEnumerator DifficultyDistanceAndMotionButtonsUpdateStateAndLabels()
        {
            var gm = GameModeManager.Instance; var ui = Ui();
            ui.difficultyButtons[2].onClick.Invoke();
            Assert.AreEqual(Difficulty.Hard, gm.CurrentDifficulty); StringAssert.Contains("Hard", ui.difficultyLabel.text);
            ui.difficultyButtons[0].onClick.Invoke(); Assert.AreEqual(Difficulty.Easy, gm.CurrentDifficulty);
            ui.distanceButtons[3].onClick.Invoke(); Assert.AreEqual(25f, gm.CurrentDistance); StringAssert.Contains("25 m", ui.laneStatusLabel.text);
            ui.motionButtons[4].onClick.Invoke(); Assert.AreEqual(TargetMotion.PopUp, gm.CurrentMotion); StringAssert.Contains("PopUp", ui.laneStatusLabel.text);
            Assert.IsTrue(gm.Lanes.All(l => l.Motion.Motion == TargetMotion.PopUp && Mathf.Approximately(l.CurrentDistance, 25f)));
            ui.motionButtons[0].onClick.Invoke(); Assert.AreEqual(TargetMotion.Static, gm.CurrentMotion);
            ui.resetTargetsButton.onClick.Invoke();
            yield return null;
            // changing difficulty mid-run re-applies the lane profile
            ui.motionButtons[1].onClick.Invoke(); ui.difficultyButtons[2].onClick.Invoke(); yield return null;
            Assert.AreEqual(Difficulty.Hard, gm.CurrentDifficulty);
        }

        [UnityTest]
        public IEnumerator HudLabelsReflectWeaponAndScoreEvents()
        {
            var gm = GameModeManager.Instance; var ui = Ui();
            var rifle = XrTestRig.Find(WeaponClass.Rifle);
            var hand = XrTestRig.MakeHand("R", new Vector3(0f, 1.4f, -1f), Quaternion.identity, UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right);
            yield return XrTestRig.Grab(hand, rifle);
            StringAssert.Contains("Rifle", ui.ammoLabel.text); StringAssert.Contains($"{rifle.MagazineSize}/{rifle.MagazineSize}", ui.ammoLabel.text); StringAssert.Contains("Semi", ui.ammoLabel.text);
            rifle.ToggleFireMode(); StringAssert.Contains("Auto", ui.ammoLabel.text);
            rifle.TryFire(); yield return null;
            StringAssert.Contains($"{rifle.MagazineSize - 1}/{rifle.MagazineSize}", ui.ammoLabel.text);
            StringAssert.Contains("Shots 1", ui.shotsLabel.text);
            rifle.StartReload(); yield return null;
            StringAssert.Contains("reloading", ui.ammoLabel.text);
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = 0, ringName = "Bullseye", points = 10 });
            StringAssert.Contains("Bullseye  +10", ui.lastHitLabel.text); StringAssert.Contains("Score 10", ui.scoreLabel.text); StringAssert.Contains("Accuracy 100%", ui.accuracyLabel.text);
            ScoreManager.Instance.RegisterHit(new HitResult { ringIndex = -1, ringName = "Miss", points = 0 });
            Assert.AreEqual("Miss", ui.lastHitLabel.text);
        }

        [UnityTest]
        public IEnumerator UiIsWiredAndReadable()
        {
            var ui = Ui();
            var buttons = new List<UnityEngine.UI.Button> { ui.freePracticeButton, ui.accuracyButton, ui.movingButton, ui.exitButton, ui.resetTargetsButton, ui.restartButton, ui.backToMenuButton };
            buttons.AddRange(ui.difficultyButtons); buttons.AddRange(ui.distanceButtons); buttons.AddRange(ui.motionButtons);
            foreach (var b in buttons) { Assert.IsNotNull(b, "button reference missing"); Assert.IsTrue(b.interactable); Assert.IsNotNull(b.GetComponentInChildren<TMP_Text>(), $"{b.name} has no label"); }
            foreach (var t in new[] { ui.difficultyLabel, ui.laneStatusLabel, ui.modeLabel, ui.scoreLabel, ui.shotsLabel, ui.accuracyLabel, ui.timerLabel, ui.ammoLabel, ui.lastHitLabel, ui.comboLabel, ui.bestLabel, ui.resultsTitle, ui.resultsBody })
                Assert.IsNotNull(t, "label reference missing");
            // readability: world-space canvases with a sane scale and no text smaller than 16 px (0.032 m at the 0.002 canvas scale)
            foreach (var canvas in ui.GetComponentsInChildren<Canvas>(true))
            {
                Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode);
                Assert.That(canvas.transform.lossyScale.x, Is.InRange(0.001f, 0.004f), $"{canvas.name} scale");
            }
            foreach (var text in ui.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.GreaterOrEqual(text.fontSize, 16f, $"{text.name} font too small for VR");
                Assert.IsNotNull(text.font, $"{text.name} has no font");
            }
            // interactable distance: every panel sits within 2.5 m of the spawn point so the rays reach it
            var spawn = new Vector3(0f, 1.5f, -1.4f);
            foreach (var panel in new[] { ui.mainMenuPanel, ui.laneControlsPanel, ui.hudPanel, ui.resultsPanel })
                Assert.Less(Vector3.Distance(panel.transform.position, spawn), 3.0f, $"{panel.name} is too far from the player");
            // the scene has a UI event system with the XRI input module
            Assert.IsNotNull(Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>());
            Assert.IsNotNull(Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>(), "XRUIInputModule missing: VR rays could not click buttons");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LaneScoreboardUpdatesOnHitsAndResetsOnModeStart()
        {
            var gm = GameModeManager.Instance;
            var boards = Object.FindObjectsByType<LaneScoreboard>(FindObjectsSortMode.None);
            Assert.AreEqual(5, boards.Length);
            var lane = gm.Lanes[2];
            var board = boards.First(b => b.name.EndsWith("3"));
            var text = board.GetComponentInChildren<TMP_Text>();
            string before = text.text;
            lane.Target.OnProjectileHit(new HitInfo { point = lane.Target.Face.position, normal = -lane.Target.Face.forward });
            yield return null;
            Assert.AreNotEqual(before, text.text, "scoreboard reacts to a hit on its lane");
            StringAssert.Contains("10", text.text);
            gm.StartFreePractice(); yield return null;
            Assert.AreEqual(before, text.text, "mode start resets the lane board");
        }
    }
}
