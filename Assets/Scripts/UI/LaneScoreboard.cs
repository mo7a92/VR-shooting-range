using TMPro;
using UnityEngine;
using VRRange.Scoring;
using VRRange.Targets;

namespace VRRange.UI
{
    /// <summary>Digital scoreboard above a lane: hits, points and last ring for that lane's target.</summary>
    public class LaneScoreboard : MonoBehaviour
    {
        [SerializeField] TargetLane m_Lane;
        [SerializeField] TMP_Text m_Text;
        int m_Hits, m_Points; string m_Last = "";

        public void Initialize(TargetLane lane, TMP_Text text) { m_Lane = lane; m_Text = text; }

        void OnEnable()
        {
            if (m_Lane != null && m_Lane.Target != null) m_Lane.Target.Hit += OnHit;
            if (Modes.GameModeManager.Instance != null) Modes.GameModeManager.Instance.ModeStarted += OnModeStarted;
            Refresh();
        }

        void Start()
        {
            if (Modes.GameModeManager.Instance != null) { Modes.GameModeManager.Instance.ModeStarted -= OnModeStarted; Modes.GameModeManager.Instance.ModeStarted += OnModeStarted; }
        }

        void OnDisable()
        {
            if (m_Lane != null && m_Lane.Target != null) m_Lane.Target.Hit -= OnHit;
            if (Modes.GameModeManager.Instance != null) Modes.GameModeManager.Instance.ModeStarted -= OnModeStarted;
        }

        void OnModeStarted(Core.GameMode mode) { m_Hits = 0; m_Points = 0; m_Last = ""; Refresh(); }

        void OnHit(BullseyeTarget t, HitResult r)
        {
            if (r.points > 0) { m_Hits++; m_Points += r.points; }
            m_Last = r.points > 0 ? $"{r.ringName} +{r.points}" : "MISS";
            Refresh();
        }

        void Refresh()
        {
            if (m_Text == null) return;
            int lane = m_Lane != null ? m_Lane.LaneIndex + 1 : 0;
            float dist = m_Lane != null ? m_Lane.CurrentDistance : 0f;
            m_Text.text = $"LANE {lane}   {dist:0} m\n<size=140%>{m_Points:000}</size>\nHITS {m_Hits:00}   {m_Last}";
        }
    }
}
