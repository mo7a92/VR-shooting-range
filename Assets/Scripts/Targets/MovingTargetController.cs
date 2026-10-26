using UnityEngine;
using VRRange.Core;

namespace VRRange.Targets
{
    /// <summary>
    /// Drives a target carrier along its lane: horizontal sweep, near/far travel, bounded random waypoints
    /// or timed pop-ups. Motion is clamped to the lane so targets never leave the range, and Reset() always
    /// returns to the exact same pose.
    /// </summary>
    public class MovingTargetController : MonoBehaviour
    {
        [SerializeField] Transform m_Carrier;     // moves laterally / in depth
        [SerializeField] Transform m_PopUpPivot;  // rotates the face down when hidden
        [SerializeField] float m_LateralLimit = 1.2f;
        [SerializeField] float m_NearDistance = 5f;
        [SerializeField] float m_FarDistance = 25f;
        [SerializeField] int m_RandomSeed = 1234;

        TargetMotion m_Motion = TargetMotion.Static;
        GameModeSettings.DifficultyProfile m_Profile = new GameModeSettings.DifficultyProfile();
        float m_BaseDistance = 10f;
        float m_Time;
        Vector3 m_Waypoint;
        float m_PopTimer; bool m_PopVisible = true;
        System.Random m_Random;
        float m_LateralRange = 0.9f;

        public TargetMotion Motion => m_Motion;
        public bool IsExposed => m_Motion != TargetMotion.PopUp || m_PopVisible;
        public Transform Carrier => m_Carrier != null ? m_Carrier : transform;

        public void Initialize(Transform carrier, Transform popUpPivot, float lateralLimit, float near, float far)
        {
            m_Carrier = carrier; m_PopUpPivot = popUpPivot; m_LateralLimit = lateralLimit; m_NearDistance = near; m_FarDistance = far;
        }

        /// <summary>Base distance from the firing line (metres) when static or as centre of motion.</summary>
        public void SetBaseDistance(float distance)
        {
            m_BaseDistance = Mathf.Clamp(distance, m_NearDistance, m_FarDistance);
            ResetMotion();
        }

        public void Configure(TargetMotion motion, GameModeSettings.DifficultyProfile profile)
        {
            m_Motion = motion;
            m_Profile = profile ?? new GameModeSettings.DifficultyProfile();
            m_LateralRange = Mathf.Min(m_LateralLimit, m_Profile.lateralRange);
            ResetMotion();
        }

        public void ResetMotion()
        {
            m_Time = 0f;
            m_Random = new System.Random(m_RandomSeed);
            m_PopTimer = m_Profile.popUpExposure; m_PopVisible = true;
            Carrier.localPosition = new Vector3(0f, 0f, m_BaseDistance);
            if (m_PopUpPivot != null) m_PopUpPivot.localRotation = Quaternion.identity;
            m_Waypoint = Carrier.localPosition;
            if (m_Motion == TargetMotion.Random) PickWaypoint();
        }

        void PickWaypoint()
        {
            float x = (float)(m_Random.NextDouble() * 2.0 - 1.0) * m_LateralRange;
            float z = Mathf.Lerp(Mathf.Max(m_NearDistance, m_BaseDistance - 5f), Mathf.Min(m_FarDistance, m_BaseDistance + 5f), (float)m_Random.NextDouble());
            m_Waypoint = new Vector3(x, 0f, z);
        }

        void Update()
        {
            if (m_Motion == TargetMotion.Static) return;
            float dt = Time.deltaTime;
            m_Time += dt;
            var carrier = Carrier;
            var pos = carrier.localPosition;
            float speed = Mathf.Max(0.05f, m_Profile.moveSpeed);
            switch (m_Motion)
            {
                case TargetMotion.Horizontal:
                {
                    float period = (4f * m_LateralRange) / speed; // full sweep there and back
                    float phase = (m_Time / Mathf.Max(0.1f, period)) * Mathf.PI * 2f;
                    pos = new Vector3(Mathf.Sin(phase) * m_LateralRange, 0f, m_BaseDistance);
                    break;
                }
                case TargetMotion.NearFar:
                {
                    float near = Mathf.Max(m_NearDistance, m_BaseDistance - 6f), far = Mathf.Min(m_FarDistance, m_BaseDistance + 6f);
                    float period = 2f * (far - near) / speed;
                    float t01 = 0.5f - 0.5f * Mathf.Cos((m_Time / Mathf.Max(0.1f, period)) * Mathf.PI * 2f);
                    pos = new Vector3(0f, 0f, Mathf.Lerp(near, far, t01));
                    break;
                }
                case TargetMotion.Random:
                {
                    pos = Vector3.MoveTowards(pos, m_Waypoint, speed * dt);
                    if ((pos - m_Waypoint).sqrMagnitude < 0.0004f) PickWaypoint();
                    break;
                }
                case TargetMotion.PopUp:
                {
                    m_PopTimer -= dt;
                    if (m_PopTimer <= 0f)
                    {
                        m_PopVisible = !m_PopVisible;
                        float jitter = 0.7f + 0.6f * (float)m_Random.NextDouble();
                        m_PopTimer = m_PopVisible ? m_Profile.popUpExposure : m_Profile.popUpHidden * jitter;
                        if (m_PopVisible && m_Profile.lateralRange > 0f)
                            pos = new Vector3((float)(m_Random.NextDouble() * 2.0 - 1.0) * m_LateralRange, 0f, m_BaseDistance);
                    }
                    if (m_PopUpPivot != null)
                    {
                        var target = Quaternion.Euler(m_PopVisible ? 0f : 90f, 0f, 0f);
                        m_PopUpPivot.localRotation = Quaternion.RotateTowards(m_PopUpPivot.localRotation, target, 360f * dt);
                    }
                    break;
                }
            }
            pos.x = Mathf.Clamp(pos.x, -m_LateralLimit, m_LateralLimit);
            pos.z = Mathf.Clamp(pos.z, m_NearDistance, m_FarDistance);
            carrier.localPosition = pos;
        }
    }
}
