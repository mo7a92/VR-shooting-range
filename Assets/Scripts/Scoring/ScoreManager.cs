using System;
using UnityEngine;

namespace VRRange.Scoring
{
    [Serializable]
    public struct SessionStats
    {
        public int shotsFired;
        public int hits;
        public int score;
        public float elapsedSeconds;
        public int bullseyes;
        public float Accuracy => shotsFired > 0 ? (float)hits / shotsFired : 0f;
        public float AccuracyPercent => Accuracy * 100f;
    }

    public struct HitResult
    {
        public int ringIndex;
        public string ringName;
        public int points;
        public float distance;
        public Vector3 worldPoint;
    }

    /// <summary>Scene singleton that counts shots, hits, score and time. Projectiles only ever report once.</summary>
    public class ScoreManager : MonoBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        public event Action<SessionStats> StatsChanged;
        public event Action<HitResult> HitRegistered;
        public event Action ShotFired;

        SessionStats m_Stats;
        bool m_Timing;
        float m_ScoreMultiplier = 1f;

        public SessionStats Stats => m_Stats;
        public bool IsTiming => m_Timing;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!m_Timing) return;
            m_Stats.elapsedSeconds += Time.deltaTime;
        }

        public void BeginSession(float scoreMultiplier = 1f)
        {
            m_Stats = default; m_ScoreMultiplier = Mathf.Max(0.1f, scoreMultiplier); m_Timing = true;
            StatsChanged?.Invoke(m_Stats);
        }

        public void StopTiming() => m_Timing = false;
        public void ResumeTiming() => m_Timing = true;

        public void ResetStats()
        {
            m_Stats = default;
            StatsChanged?.Invoke(m_Stats);
        }

        public void RegisterShot()
        {
            m_Stats.shotsFired++;
            ShotFired?.Invoke();
            StatsChanged?.Invoke(m_Stats);
        }

        public void RegisterHit(HitResult result)
        {
            if (result.points > 0)
            {
                m_Stats.hits++;
                m_Stats.score += Mathf.RoundToInt(result.points * m_ScoreMultiplier);
                if (result.ringIndex == 0) m_Stats.bullseyes++;
            }
            HitRegistered?.Invoke(result);
            StatsChanged?.Invoke(m_Stats);
        }

        public static void NotifyShot() { if (Instance != null) Instance.RegisterShot(); }
        public static void NotifyHit(HitResult r) { if (Instance != null) Instance.RegisterHit(r); }
    }
}
