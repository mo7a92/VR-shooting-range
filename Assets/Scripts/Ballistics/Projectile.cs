using UnityEngine;
using VRRange.Targets;

namespace VRRange.Ballistics
{
    public struct ProjectileParams
    {
        public float speed;
        public float gravityMultiplier;
        public float lifetime;
        public Color tracerColor;
    }

    /// <summary>
    /// Pooled, non-rigidbody projectile: integrates its own motion and sweeps a raycast between frames so fast
    /// shots never tunnel. It reports exactly one hit, then returns to the pool.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        [SerializeField] TrailRenderer m_Trail;
        [SerializeField] Renderer m_Renderer;

        Vector3 m_Velocity;
        Vector3 m_Gravity;
        Vector3 m_Origin;
        float m_Age, m_Lifetime;
        bool m_Consumed;
        LayerMask m_Mask;
        Weapons.Weapon m_Source;
        ProjectilePool m_Pool;

        static readonly RaycastHit[] s_Hits = new RaycastHit[8];

        public bool IsActive => !m_Consumed && gameObject.activeSelf;

        public void Launch(ProjectilePool pool, Vector3 position, Vector3 direction, in ProjectileParams p, LayerMask mask, Weapons.Weapon source)
        {
            m_Pool = pool; m_Source = source; m_Mask = mask;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            m_Origin = position;
            m_Velocity = direction.normalized * p.speed;
            m_Gravity = Physics.gravity * p.gravityMultiplier;
            m_Lifetime = p.lifetime; m_Age = 0f; m_Consumed = false;
            if (m_Trail != null)
            {
                m_Trail.Clear();
                m_Trail.startColor = p.tracerColor;
                m_Trail.endColor = new Color(p.tracerColor.r, p.tracerColor.g, p.tracerColor.b, 0f);
                m_Trail.emitting = true;
            }
            if (m_Renderer != null) m_Renderer.enabled = true;
        }

        void Update()
        {
            if (m_Consumed) return;
            float dt = Time.deltaTime;
            m_Age += dt;
            Vector3 last = transform.position;
            Vector3 pos = last, vel = m_Velocity;
            BallisticsMath.Integrate(ref pos, ref vel, dt, m_Gravity);
            Vector3 delta = pos - last;
            float dist = delta.magnitude;
            if (dist > 1e-5f)
            {
                int count = Physics.RaycastNonAlloc(last, delta / dist, s_Hits, dist, m_Mask, QueryTriggerInteraction.Ignore);
                int best = -1; float bestDist = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    var h = s_Hits[i];
                    if (m_Source != null && h.collider != null && h.collider.transform.IsChildOf(m_Source.transform)) continue; // never hit the firing weapon
                    if (h.distance < bestDist) { bestDist = h.distance; best = i; }
                }
                if (best >= 0)
                {
                    OnHit(s_Hits[best]);
                    return;
                }
            }
            m_Velocity = vel;
            transform.position = pos;
            if (vel.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(vel);
            if (m_Age >= m_Lifetime || pos.y < -20f) Despawn();
        }

        void OnHit(RaycastHit hit)
        {
            m_Consumed = true;
            transform.position = hit.point;
            var info = new HitInfo
            {
                point = hit.point, normal = hit.normal, collider = hit.collider,
                travelDistance = Vector3.Distance(m_Origin, hit.point), travelTime = m_Age, weapon = m_Source
            };
            ImpactEffects.Spawn(hit, m_Velocity);
            var receiver = hit.collider != null ? hit.collider.GetComponentInParent<IHitReceiver>() : null;
            if (receiver != null)
            {
                try { receiver.OnProjectileHit(info); }
                catch (System.Exception e) { Debug.LogException(e, this); }
            }
            Despawn();
        }

        void Despawn()
        {
            m_Consumed = true;
            if (m_Trail != null) m_Trail.emitting = false;
            if (m_Pool != null) m_Pool.Release(this); else gameObject.SetActive(false);
        }
    }
}
