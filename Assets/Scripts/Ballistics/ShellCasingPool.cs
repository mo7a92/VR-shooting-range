using UnityEngine;
using VRRange.Audio;
using VRRange.Pooling;

namespace VRRange.Ballistics
{
    /// <summary>Pooled ejected shell casings: small rigidbodies thrown from the weapon's ejection port, recycled after a short life.</summary>
    public class ShellCasingPool : MonoBehaviour
    {
        public static ShellCasingPool Instance { get; private set; }

        [SerializeField] Rigidbody m_CasingPrefab;
        [SerializeField, Range(8, 128)] int m_Prewarm = 24;
        [SerializeField, Range(0.5f, 10f)] float m_Lifetime = 4f;
        [SerializeField] AudioClip[] m_BounceClips;

        ComponentPool<Rigidbody> m_Pool;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (m_CasingPrefab != null) m_Pool = new ComponentPool<Rigidbody>(m_CasingPrefab, transform, m_Prewarm, 96);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void Eject(Vector3 position, Vector3 velocity, Vector3 angularVelocity, float scale = 1f)
        {
            if (Instance == null || Instance.m_Pool == null) return;
            var rb = Instance.m_Pool.Get();
            rb.transform.SetPositionAndRotation(position, Random.rotation);
            rb.transform.localScale = Vector3.one * scale;
            rb.linearVelocity = velocity; rb.angularVelocity = angularVelocity;
            var c = rb.GetComponent<ShellCasing>();
            if (c != null) c.Arm(Instance, Instance.m_Lifetime);
            Instance.StartCoroutine(Instance.ReleaseLater(rb, Instance.m_Lifetime));
        }

        System.Collections.IEnumerator ReleaseLater(Rigidbody rb, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; m_Pool.Release(rb); }
        }

        public void PlayBounce(Vector3 at)
        {
            if (m_BounceClips == null || m_BounceClips.Length == 0) return;
            AudioManager.PlayAt(m_BounceClips[Random.Range(0, m_BounceClips.Length)], at, 0.35f, 0.15f);
        }
    }
}
