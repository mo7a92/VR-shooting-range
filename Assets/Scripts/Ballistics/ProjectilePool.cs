using UnityEngine;
using VRRange.Pooling;

namespace VRRange.Ballistics
{
    /// <summary>Scene singleton that owns the projectile pool. Weapons call <see cref="Fire"/>.</summary>
    public class ProjectilePool : MonoBehaviour
    {
        public static ProjectilePool Instance { get; private set; }

        [SerializeField] Projectile m_ProjectilePrefab;
        [SerializeField, Range(8, 256)] int m_Prewarm = 48;
        [Tooltip("Layers projectiles can hit. Leave empty to hit everything except the Projectile and Weapon layers.")]
        [SerializeField] LayerMask m_HitMask = 0;

        ComponentPool<Projectile> m_Pool;

        public LayerMask HitMask => m_HitMask;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (m_HitMask.value == 0)
            {
                int mask = ~0;
                int lp = LayerMask.NameToLayer("Projectile"); if (lp >= 0) mask &= ~(1 << lp);
                int lw = LayerMask.NameToLayer("Weapon"); if (lw >= 0) mask &= ~(1 << lw);
                int li = LayerMask.NameToLayer("Ignore Raycast"); if (li >= 0) mask &= ~(1 << li);
                m_HitMask = mask;
            }
            if (m_ProjectilePrefab == null)
            {
                Debug.LogError("[VRRange] ProjectilePool has no projectile prefab; weapons will fire without visible projectiles.", this);
                return;
            }
            m_Pool = new ComponentPool<Projectile>(m_ProjectilePrefab, transform, m_Prewarm);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public Projectile Fire(Vector3 position, Vector3 direction, in ProjectileParams p, Weapons.Weapon source)
        {
            if (m_Pool == null) return null;
            var proj = m_Pool.Get();
            proj.Launch(this, position, direction, p, m_HitMask, source);
            return proj;
        }

        public void Release(Projectile projectile) => m_Pool?.Release(projectile);
    }
}
