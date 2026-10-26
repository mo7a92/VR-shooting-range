using System.Collections.Generic;
using UnityEngine;
using VRRange.Audio;
using VRRange.Core;
using VRRange.Pooling;

namespace VRRange.Ballistics
{
    /// <summary>Scene singleton: pooled bullet-hole decals, impact particles and surface-dependent impact audio.</summary>
    public class ImpactEffects : MonoBehaviour
    {
        [System.Serializable]
        public class SurfaceEffect
        {
            public SurfaceType surface;
            public AudioClip[] clips;
            public Color particleColor = Color.gray;
            public bool leaveDecal = true;
        }

        public static ImpactEffects Instance { get; private set; }

        [SerializeField] GameObject m_DecalPrefab;
        [SerializeField] ParticleSystem m_ParticlePrefab;
        [SerializeField] SurfaceEffect[] m_Surfaces;
        [SerializeField, Range(8, 512)] int m_MaxDecals = 160;
        [SerializeField, Range(0.005f, 0.1f)] float m_DecalSize = 0.025f;
        [SerializeField, Range(0f, 1f)] float m_Volume = 0.8f;

        ComponentPool<Transform> m_Decals;
        ComponentPool<ParticleSystem> m_Particles;
        readonly Queue<Transform> m_ActiveDecals = new Queue<Transform>();
        readonly Dictionary<SurfaceType, SurfaceEffect> m_Lookup = new Dictionary<SurfaceType, SurfaceEffect>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (m_DecalPrefab != null) m_Decals = new ComponentPool<Transform>(m_DecalPrefab.transform, transform, 32, m_MaxDecals + 8);
            if (m_ParticlePrefab != null) m_Particles = new ComponentPool<ParticleSystem>(m_ParticlePrefab, transform, 12, 64);
            if (m_Surfaces != null) foreach (var s in m_Surfaces) if (s != null) m_Lookup[s.surface] = s;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void Spawn(RaycastHit hit, Vector3 velocity)
        {
            if (Instance == null) return;
            Instance.SpawnInternal(hit, velocity);
        }

        void SpawnInternal(RaycastHit hit, Vector3 velocity)
        {
            var tag = hit.collider != null ? hit.collider.GetComponentInParent<SurfaceMaterialTag>() : null;
            var surface = tag != null ? tag.surface : SurfaceType.Generic;
            m_Lookup.TryGetValue(surface, out var fx);
            if (fx == null) m_Lookup.TryGetValue(SurfaceType.Generic, out fx);

            if (m_Decals != null && (fx == null || fx.leaveDecal))
            {
                var d = m_Decals.Get();
                d.SetPositionAndRotation(hit.point + hit.normal * 0.002f, Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
                d.localScale = Vector3.one * m_DecalSize;
                // Follow moving targets: parent to the hit collider so holes travel with the target.
                if (hit.collider != null) d.SetParent(hit.collider.transform, true);
                m_ActiveDecals.Enqueue(d);
                while (m_ActiveDecals.Count > m_MaxDecals) m_Decals.Release(m_ActiveDecals.Dequeue());
            }
            if (m_Particles != null)
            {
                var ps = m_Particles.Get();
                ps.transform.SetPositionAndRotation(hit.point, Quaternion.LookRotation(hit.normal));
                var main = ps.main;
                main.startColor = fx != null ? fx.particleColor : Color.gray;
                ps.Play(true);
                StartCoroutine(ReleaseParticleLater(ps, main.duration + main.startLifetime.constantMax + 0.1f));
            }
            if (fx != null && fx.clips != null && fx.clips.Length > 0)
                AudioManager.PlayAt(fx.clips[Random.Range(0, fx.clips.Length)], hit.point, m_Volume, 0.1f);
        }

        System.Collections.IEnumerator ReleaseParticleLater(ParticleSystem ps, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (ps != null) m_Particles.Release(ps);
        }

        /// <summary>Removes decals attached to a transform (used when a target is reset).</summary>
        public void ClearDecalsUnder(Transform root)
        {
            if (m_Decals == null || root == null) return;
            int n = m_ActiveDecals.Count;
            for (int i = 0; i < n; i++)
            {
                var d = m_ActiveDecals.Dequeue();
                if (d == null) continue;
                if (d.IsChildOf(root)) m_Decals.Release(d); else m_ActiveDecals.Enqueue(d);
            }
        }
    }
}
