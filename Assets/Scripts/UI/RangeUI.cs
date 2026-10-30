using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRRange.Audio;
using VRRange.Core;
using VRRange.Modes;
using VRRange.Scoring;
using VRRange.Weapons;

namespace VRRange.UI
{
    /// <summary>
    /// Binds the world-space menu / HUD / results canvases to the managers. All references are assigned by the
    /// scene builder; any missing reference is tolerated.
    /// </summary>
    public class RangeUI : MonoBehaviour
    {
        [Header("Panels")]
        public GameObject mainMenuPanel;
        public GameObject hudPanel;
        public GameObject resultsPanel;
        public GameObject laneControlsPanel;

        [Header("Main menu")]
        public Button freePracticeButton;
        public Button accuracyButton;
        public Button movingButton;
        public Button[] difficultyButtons;
        public Button exitButton;
        public TMP_Text difficultyLabel;

        [Header("Lane controls")]
        public Button[] distanceButtons;
        public Button[] motionButtons;
        public Button resetTargetsButton;
        public TMP_Text laneStatusLabel;

        [Header("HUD")]
        public TMP_Text modeLabel;
        public TMP_Text scoreLabel;
        public TMP_Text shotsLabel;
        public TMP_Text accuracyLabel;
        public TMP_Text timerLabel;
        public TMP_Text ammoLabel;
        public TMP_Text lastHitLabel;
        public TMP_Text hintLabel;
        public TMP_Text comboLabel;
        public TMP_Text bestLabel;

        [Header("Results")]
        public TMP_Text resultsTitle;
        public TMP_Text resultsBody;
        public Button restartButton;
        public Button backToMenuButton;

        float m_LastHitTimer;
        Weapon m_ActiveWeapon;
        readonly StringBuilder m_Sb = new StringBuilder(128);

        void Start()
        {
            var gm = GameModeManager.Instance;
            var sm = ScoreManager.Instance;
            Bind(freePracticeButton, () => { gm?.StartFreePractice(); ShowHud(); });
            Bind(accuracyButton, () => { gm?.StartAccuracyChallenge(); ShowHud(); });
            Bind(movingButton, () => { gm?.StartMovingTargetChallenge(); ShowHud(); });
            Bind(exitButton, QuitApplication);
            Bind(resetTargetsButton, () => gm?.ResetTargets());
            Bind(restartButton, () => { gm?.Restart(); ShowHud(); });
            Bind(backToMenuButton, ShowMenu);
            if (difficultyButtons != null)
                for (int i = 0; i < difficultyButtons.Length; i++) { int idx = i; Bind(difficultyButtons[i], () => { gm?.SetDifficultyIndex(idx); RefreshLabels(); }); }
            if (distanceButtons != null)
                for (int i = 0; i < distanceButtons.Length; i++) { int idx = i; Bind(distanceButtons[i], () => { gm?.SetPracticeDistanceIndex(idx); RefreshLabels(); }); }
            if (motionButtons != null)
                for (int i = 0; i < motionButtons.Length; i++) { int idx = i; Bind(motionButtons[i], () => { gm?.SetPracticeMotionIndex(idx); RefreshLabels(); }); }

            if (gm != null)
            {
                gm.StateChanged += OnStateChanged;
                gm.ModeFinished += OnModeFinished;
                gm.ModeStarted += _ => RefreshLabels();
                gm.CountdownTick += t => SetText(timerLabel, t > 0f ? $"Ready: {Mathf.CeilToInt(t)}" : "GO!");
                gm.CountdownSecond += s2 => { SetText(lastHitLabel, s2 > 0 ? s2.ToString() : "GO!"); m_LastHitTimer = 1f; };
                gm.ComboChanged += c => SetText(comboLabel, c >= 3 ? $"COMBO x{c}" : "");
                foreach (var w in gm.Weapons) HookWeapon(w);
            }
            if (sm != null) { sm.StatsChanged += OnStatsChanged; sm.HitRegistered += OnHit; }
            SetText(hintLabel, "Grip = grab weapon   Trigger = fire   A/X = reload   B/Y = fire mode\nLeft stick = teleport   Right stick = snap turn   Point at panels to use buttons");
            ShowMenu();
            RefreshLabels();
            if (sm != null) OnStatsChanged(sm.Stats);
        }

        void OnDestroy()
        {
            var gm = GameModeManager.Instance; var sm = ScoreManager.Instance;
            if (gm != null) { gm.StateChanged -= OnStateChanged; gm.ModeFinished -= OnModeFinished; }
            if (sm != null) { sm.StatsChanged -= OnStatsChanged; sm.HitRegistered -= OnHit; }
        }

        void Update()
        {
            var gm = GameModeManager.Instance;
            if (gm != null && gm.State == ModeState.Running)
            {
                if (gm.CurrentMode == GameMode.MovingTargetChallenge) SetText(timerLabel, $"Time {gm.TimeRemaining:0.0}s");
                else if (ScoreManager.Instance != null) SetText(timerLabel, $"Time {FormatTime(ScoreManager.Instance.Stats.elapsedSeconds)}");
            }
            if (m_LastHitTimer > 0f) { m_LastHitTimer -= Time.deltaTime; if (m_LastHitTimer <= 0f) SetText(lastHitLabel, ""); }
        }

        void HookWeapon(Weapon w)
        {
            if (w == null) return;
            w.AmmoChanged += (wp, rounds, size) => { m_ActiveWeapon = wp; SetText(ammoLabel, $"{wp.DisplayName}  {rounds}/{size}  [{wp.CurrentFireMode}]"); };
            w.ReloadStateChanged += (wp, reloading) => { if (reloading) SetText(ammoLabel, $"{wp.DisplayName}  reloading..."); };
            w.FireModeChanged += (wp, mode) => SetText(ammoLabel, $"{wp.DisplayName}  {wp.Rounds}/{wp.MagazineSize}  [{mode}]");
        }

        void OnStatsChanged(SessionStats s)
        {
            var gm = GameModeManager.Instance;
            SetText(scoreLabel, $"Score {s.score}");
            string shots = gm != null && gm.ShotLimit > 0 ? $"Shots {s.shotsFired}/{gm.ShotLimit}" : $"Shots {s.shotsFired}";
            SetText(shotsLabel, $"{shots}   Hits {s.hits}");
            SetText(accuracyLabel, $"Accuracy {s.AccuracyPercent:0}%");
        }

        void OnHit(HitResult r)
        {
            SetText(lastHitLabel, r.points > 0 ? $"{r.ringName}  +{r.points}" : "Miss");
            m_LastHitTimer = 1.5f;
        }

        void OnStateChanged(ModeState state)
        {
            if (state == ModeState.Finished) ShowResults();
        }

        void OnModeFinished(ModeResult r)
        {
            SetText(resultsTitle, $"{ModeName(r.mode)} complete");
            m_Sb.Clear();
            m_Sb.Append("Score: ").Append(r.stats.score);
            if (r.maxPossibleScore > 0) m_Sb.Append(" / ").Append(r.maxPossibleScore);
            m_Sb.Append("\nShots: ").Append(r.stats.shotsFired).Append("   Hits: ").Append(r.stats.hits);
            m_Sb.Append("\nAccuracy: ").Append(r.stats.AccuracyPercent.ToString("0")).Append('%');
            m_Sb.Append("\nBullseyes: ").Append(r.stats.bullseyes);
            m_Sb.Append("\nTime: ").Append(FormatTime(r.stats.elapsedSeconds));
            m_Sb.Append("\nDifficulty: ").Append(r.difficulty).Append("   Grade: ").Append(r.grade);
            m_Sb.Append("\nBest combo: ").Append(r.bestCombo);
            m_Sb.Append(r.isNewPersonalBest ? "\n<color=#F5C81A>NEW PERSONAL BEST!</color>" : $"\nPersonal best: {r.personalBest}");
            SetText(resultsBody, m_Sb.ToString());
        }

        void RefreshLabels()
        {
            var gm = GameModeManager.Instance;
            if (gm == null) return;
            SetText(modeLabel, ModeName(gm.CurrentMode));
            SetText(difficultyLabel, $"Difficulty: {gm.CurrentDifficulty}");
            SetText(laneStatusLabel, $"Distance {gm.CurrentDistance:0} m   Motion: {gm.CurrentMotion}");
            int best = GameModeManager.PersonalBest(gm.CurrentMode, gm.CurrentDifficulty);
            SetText(bestLabel, best > 0 ? $"Personal best ({gm.CurrentDifficulty}): {best}" : "No personal best yet");
            if (laneControlsPanel != null) laneControlsPanel.SetActive(gm.CurrentMode == GameMode.FreePractice);
        }

        public void ShowMenu()
        {
            Toggle(mainMenuPanel, true); Toggle(resultsPanel, false); Toggle(hudPanel, true);
            RefreshLabels();
        }

        public void ShowHud()
        {
            Toggle(mainMenuPanel, false); Toggle(resultsPanel, false); Toggle(hudPanel, true);
            RefreshLabels();
        }

        public void ShowResults()
        {
            Toggle(resultsPanel, true); Toggle(mainMenuPanel, false);
        }

        static void Toggle(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }
        static void SetText(TMP_Text t, string s) { if (t != null) t.text = s; }
        static void Bind(Button b, UnityEngine.Events.UnityAction action)
        {
            if (b == null) return;
            b.onClick.AddListener(() => { AudioManager.PlayUiClick(); action?.Invoke(); });
        }
        static string FormatTime(float seconds) { int m = (int)(seconds / 60f); float s = seconds - m * 60f; return m > 0 ? $"{m}:{s:00.0}" : $"{s:0.0}s"; }
        static string ModeName(GameMode m) => m switch
        {
            GameMode.FreePractice => "Free Practice",
            GameMode.AccuracyChallenge => "Accuracy Challenge",
            GameMode.MovingTargetChallenge => "Moving Target Challenge",
            _ => m.ToString()
        };

        static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
