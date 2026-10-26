using UnityEngine;
using VRRange.Core;

namespace VRRange.Targets
{
    /// <summary>One firing lane: a carrier on a track, a bullseye target and its motion controller.</summary>
    public class TargetLane : MonoBehaviour
    {
        [SerializeField] int m_LaneIndex;
        [SerializeField] BullseyeTarget m_Target;
        [SerializeField] MovingTargetController m_Motion;
        [SerializeField] GameModeSettings m_Settings;
        [SerializeField] float m_CurrentDistance = 10f;

        public int LaneIndex => m_LaneIndex;
        public BullseyeTarget Target => m_Target;
        public MovingTargetController Motion => m_Motion;
        public float CurrentDistance => m_CurrentDistance;

        public void Initialize(int index, BullseyeTarget target, MovingTargetController motion, GameModeSettings settings)
        {
            m_LaneIndex = index; m_Target = target; m_Motion = motion; m_Settings = settings;
            m_CurrentDistance = settings != null ? settings.defaultDistance : 10f;
        }

        void Start()
        {
            SetDistance(m_CurrentDistance);
        }

        public void SetDistance(float distance)
        {
            m_CurrentDistance = m_Settings != null ? m_Settings.ClampDistance(distance) : distance;
            if (m_Motion != null) m_Motion.SetBaseDistance(m_CurrentDistance);
            if (m_Target != null) m_Target.DistanceFromShooter = m_CurrentDistance;
        }

        public void SetMotion(TargetMotion motion, Difficulty difficulty)
        {
            if (m_Motion == null) return;
            var profile = m_Settings != null ? m_Settings.Profile(difficulty) : null;
            m_Motion.Configure(motion, profile);
        }

        public void ResetLane()
        {
            m_Target?.ResetTarget();
            m_Motion?.ResetMotion();
        }
    }
}
