using System;
using System.Collections.Generic;
using UnityEngine;
using VRRange.Core;
using VRRange.Scoring;
using VRRange.Targets;
using VRRange.Weapons;

namespace VRRange.Modes
{
    public enum ModeState { Idle, Countdown, Running, Finished }

    [Serializable]
    public struct ModeResult
    {
        public GameMode mode;
        public Difficulty difficulty;
        public SessionStats stats;
        public string grade;
        public int maxPossibleScore;
        public int personalBest;
        public bool isNewPersonalBest;
        public int bestCombo;
    }

    /// <summary>
    /// Owns the three play modes. Free Practice is untimed; Accuracy Challenge ends after a fixed number of shots;
    /// Moving Target Challenge ends when the countdown expires. Lanes and weapons are reset deterministically.
    /// </summary>
    public class GameModeManager : MonoBehaviour
    {
        public static GameModeManager Instance { get; private set; }

        [SerializeField] GameModeSettings m_Settings;
        [SerializeField] List<TargetLane> m_Lanes = new List<TargetLane>();
        [SerializeField] List<Weapon> m_Weapons = new List<Weapon>();
        [SerializeField, Range(0f, 10f)] float m_CountdownSeconds = 3f;

        GameMode m_Mode = GameMode.FreePractice;
        Difficulty m_Difficulty = Difficulty.Medium;
        TargetMotion m_PracticeMotion = TargetMotion.Static;
        float m_PracticeDistance = 10f;
        ModeState m_State = ModeState.Idle;
        float m_Countdown, m_TimeRemaining;
        ModeResult m_LastResult;
        int m_LastCountdownSecond = -1;
        int m_Combo, m_BestCombo;

        public int Combo => m_Combo;
        public int BestCombo => m_BestCombo;
        public event Action<int> ComboChanged;
        public event Action<int> CountdownSecond; // 3, 2, 1, 0(go)

        public event Action<ModeState> StateChanged;
        public event Action<GameMode> ModeStarted;
        public event Action<ModeResult> ModeFinished;
        public event Action<float> CountdownTick;

        public GameModeSettings Settings => m_Settings;
        public GameMode CurrentMode => m_Mode;
        public Difficulty CurrentDifficulty => m_Difficulty;
        public ModeState State => m_State;
        public float TimeRemaining => m_TimeRemaining;
        public float CountdownRemaining => m_Countdown;
        public ModeResult LastResult => m_LastResult;
        public IReadOnlyList<TargetLane> Lanes => m_Lanes;
        public IReadOnlyList<Weapon> Weapons => m_Weapons;
        public int ShotLimit => m_Mode == GameMode.AccuracyChallenge && m_Settings != null ? m_Settings.accuracyChallengeShots : 0;
        public float CurrentDistance => m_PracticeDistance;
        public TargetMotion CurrentMotion => m_PracticeMotion;

        public void Initialize(GameModeSettings settings, List<TargetLane> lanes, List<Weapon> weapons)
        {
            m_Settings = settings; m_Lanes = lanes ?? new List<TargetLane>(); m_Weapons = weapons ?? new List<Weapon>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (m_Settings != null) m_PracticeDistance = m_Settings.defaultDistance;
        }

        void OnEnable() { if (ScoreManager.Instance != null) { ScoreManager.Instance.ShotFired += OnShotFired; ScoreManager.Instance.HitRegistered += OnHitRegistered; } }
        void OnDisable() { if (ScoreManager.Instance != null) { ScoreManager.Instance.ShotFired -= OnShotFired; ScoreManager.Instance.HitRegistered -= OnHitRegistered; } }

        void OnHitRegistered(HitResult r)
        {
            if (m_State != ModeState.Running) return;
            if (r.points > 0) { m_Combo++; m_BestCombo = Mathf.Max(m_BestCombo, m_Combo); }
            else m_Combo = 0;
            ComboChanged?.Invoke(m_Combo);
        }

        void Start()
        {
            if (ScoreManager.Instance != null) { ScoreManager.Instance.ShotFired -= OnShotFired; ScoreManager.Instance.ShotFired += OnShotFired; ScoreManager.Instance.HitRegistered -= OnHitRegistered; ScoreManager.Instance.HitRegistered += OnHitRegistered; }
            if (m_Lanes.Count == 0) m_Lanes.AddRange(FindObjectsByType<TargetLane>(FindObjectsSortMode.None));
            if (m_Weapons.Count == 0) m_Weapons.AddRange(FindObjectsByType<Weapon>(FindObjectsSortMode.None));
            StartFreePractice();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            switch (m_State)
            {
                case ModeState.Countdown:
                    m_Countdown -= Time.deltaTime;
                    CountdownTick?.Invoke(Mathf.Max(0f, m_Countdown));
                    int sec = Mathf.CeilToInt(m_Countdown);
                    if (sec != m_LastCountdownSecond) { m_LastCountdownSecond = sec; CountdownSecond?.Invoke(sec); Audio.AudioManager.PlayCountdown(sec); }
                    if (m_Countdown <= 0f) BeginRunning();
                    break;
                case ModeState.Running:
                    if (m_Mode == GameMode.MovingTargetChallenge)
                    {
                        m_TimeRemaining -= Time.deltaTime;
                        if (m_TimeRemaining <= 0f) { m_TimeRemaining = 0f; Finish(); }
                    }
                    break;
            }
        }

        // ---- public controls (bound to VR menu buttons) ----
        public void StartFreePractice() => StartMode(GameMode.FreePractice);
        public void StartAccuracyChallenge() => StartMode(GameMode.AccuracyChallenge);
        public void StartMovingTargetChallenge() => StartMode(GameMode.MovingTargetChallenge);
        public void Restart() => StartMode(m_Mode);

        public void SetDifficulty(Difficulty d)
        {
            m_Difficulty = d;
            if (m_State == ModeState.Running || m_Mode == GameMode.FreePractice) ApplyLaneSetup();
        }
        public void SetDifficultyIndex(int i) => SetDifficulty((Difficulty)Mathf.Clamp(i, 0, 2));

        /// <summary>Free Practice: choose the target distance preset.</summary>
        public void SetPracticeDistance(float metres)
        {
            m_PracticeDistance = m_Settings != null ? m_Settings.ClampDistance(metres) : metres;
            if (m_Mode == GameMode.FreePractice) ApplyLaneSetup();
        }
        public void SetPracticeDistanceIndex(int i)
        {
            if (m_Settings == null || m_Settings.distancePresets == null || m_Settings.distancePresets.Length == 0) return;
            SetPracticeDistance(m_Settings.distancePresets[Mathf.Clamp(i, 0, m_Settings.distancePresets.Length - 1)]);
        }

        /// <summary>Free Practice: choose how the targets move.</summary>
        public void SetPracticeMotion(TargetMotion motion)
        {
            m_PracticeMotion = motion;
            if (m_Mode == GameMode.FreePractice) ApplyLaneSetup();
        }
        public void SetPracticeMotionIndex(int i) => SetPracticeMotion((TargetMotion)Mathf.Clamp(i, 0, 4));

        public void ResetTargets() { foreach (var l in m_Lanes) if (l != null) l.ResetLane(); }

        public void StartMode(GameMode mode)
        {
            m_Mode = mode;
            ResetTargets();
            foreach (var w in m_Weapons) if (w != null) w.RefillInstant();
            ApplyLaneSetup();
            float mult = mode == GameMode.MovingTargetChallenge && m_Settings != null ? m_Settings.Profile(m_Difficulty).scoreMultiplier : 1f;
            ScoreManager.Instance?.BeginSession(mult);
            m_Combo = 0; m_BestCombo = 0; m_LastCountdownSecond = -1; ComboChanged?.Invoke(0);
            ModeStarted?.Invoke(mode);
            if (mode == GameMode.FreePractice)
            {
                BeginRunning();
            }
            else
            {
                ScoreManager.Instance?.StopTiming();
                m_Countdown = m_CountdownSeconds;
                SetState(ModeState.Countdown);
                CountdownTick?.Invoke(m_Countdown);
            }
        }

        void BeginRunning()
        {
            m_TimeRemaining = m_Mode == GameMode.MovingTargetChallenge && m_Settings != null ? m_Settings.movingChallengeSeconds : 0f;
            ScoreManager.Instance?.ResumeTiming();
            SetState(ModeState.Running);
        }

        void ApplyLaneSetup()
        {
            foreach (var lane in m_Lanes)
            {
                if (lane == null) continue;
                switch (m_Mode)
                {
                    case GameMode.FreePractice:
                        lane.SetDistance(m_PracticeDistance);
                        lane.SetMotion(m_PracticeMotion, m_Difficulty);
                        break;
                    case GameMode.AccuracyChallenge:
                        lane.SetDistance(m_Settings != null ? m_Settings.accuracyChallengeDistance : 10f);
                        lane.SetMotion(TargetMotion.Static, m_Difficulty);
                        break;
                    case GameMode.MovingTargetChallenge:
                        lane.SetDistance(m_Settings != null ? m_Settings.defaultDistance : 10f);
                        lane.SetMotion(m_Settings != null ? m_Settings.movingChallengeMotion : TargetMotion.Random, m_Difficulty);
                        break;
                }
            }
        }

        void OnShotFired()
        {
            if (m_State != ModeState.Running || m_Mode != GameMode.AccuracyChallenge || m_Settings == null) return;
            var stats = ScoreManager.Instance != null ? ScoreManager.Instance.Stats : default;
            if (stats.shotsFired >= m_Settings.accuracyChallengeShots)
                StartCoroutine(FinishAfterProjectilesLand());
        }

        System.Collections.IEnumerator FinishAfterProjectilesLand()
        {
            SetState(ModeState.Countdown); // block further scoring changes to mode state while last shots land
            yield return new WaitForSeconds(1.0f);
            Finish();
        }

        public void Finish()
        {
            if (m_State == ModeState.Finished) return;
            ScoreManager.Instance?.StopTiming();
            var stats = ScoreManager.Instance != null ? ScoreManager.Instance.Stats : default;
            int maxPoints = 0;
            var rules = m_Lanes.Count > 0 && m_Lanes[0] != null && m_Lanes[0].Target != null ? m_Lanes[0].Target.Rules : null;
            int perShot = rules != null ? rules.MaxPoints : 10;
            if (m_Mode == GameMode.AccuracyChallenge && m_Settings != null) maxPoints = m_Settings.accuracyChallengeShots * perShot;
            string key = $"VRRange.Best.{m_Mode}.{m_Difficulty}";
            int best = PlayerPrefs.GetInt(key, 0);
            bool newBest = stats.score > best;
            if (newBest) { PlayerPrefs.SetInt(key, stats.score); PlayerPrefs.Save(); best = stats.score; }
            m_LastResult = new ModeResult { mode = m_Mode, difficulty = m_Difficulty, stats = stats, maxPossibleScore = maxPoints, grade = GradeFor(stats, maxPoints), personalBest = best, isNewPersonalBest = newBest, bestCombo = m_BestCombo };
            SetState(ModeState.Finished);
            Audio.AudioManager.PlayRoundComplete();
            ModeFinished?.Invoke(m_LastResult);
        }

        /// <summary>Stored personal best for a mode/difficulty (0 if none).</summary>
        public static int PersonalBest(GameMode mode, Difficulty d) => PlayerPrefs.GetInt($"VRRange.Best.{mode}.{d}", 0);

        public static string GradeFor(SessionStats stats, int maxPossible)
        {
            float ratio = maxPossible > 0 ? (float)stats.score / maxPossible : stats.Accuracy;
            if (stats.shotsFired == 0) return "-";
            if (ratio >= 0.9f) return "S";
            if (ratio >= 0.75f) return "A";
            if (ratio >= 0.55f) return "B";
            if (ratio >= 0.35f) return "C";
            return "D";
        }

        void SetState(ModeState s)
        {
            if (m_State == s) return;
            m_State = s;
            StateChanged?.Invoke(s);
        }
    }
}
