using UnityEngine;

namespace VRRange.Ballistics
{
    /// <summary>Per-casing helper: plays a tinkle on the first hard bounce.</summary>
    public class ShellCasing : MonoBehaviour
    {
        ShellCasingPool m_Pool; int m_Bounces;
        public void Arm(ShellCasingPool pool, float life) { m_Pool = pool; m_Bounces = 0; }
        void OnCollisionEnter(Collision c)
        {
            if (m_Pool == null || m_Bounces > 1 || c.relativeVelocity.sqrMagnitude < 0.5f) return;
            m_Bounces++;
            m_Pool.PlayBounce(transform.position);
        }
    }
}
