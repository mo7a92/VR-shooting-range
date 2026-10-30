using UnityEngine;
using VRRange.Pooling;

namespace VRRange.Audio
{
    /// <summary>Pooled 3D one-shot audio, ambience loop and UI clicks. Safe to call when no instance exists.</summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [SerializeField] AudioSource m_OneShotPrefab;
        [SerializeField] AudioSource m_AmbienceSource;
        [SerializeField] AudioClip m_AmbienceLoop;
        [SerializeField] AudioClip m_UiClick;
        [SerializeField] AudioClip m_TargetHitDing;
        [SerializeField] AudioClip[] m_CountdownClips;   // 3, 2, 1, go
        [SerializeField] AudioClip m_RoundComplete;
        [SerializeField] AudioClip m_MenuConfirm;
        [SerializeField, Range(0f, 1f)] float m_AmbienceVolume = 0.35f;
        [SerializeField, Range(4, 64)] int m_PoolSize = 24;

        ComponentPool<AudioSource> m_Pool;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (m_OneShotPrefab != null) m_Pool = new ComponentPool<AudioSource>(m_OneShotPrefab, transform, m_PoolSize, m_PoolSize * 2);
        }

        void Start()
        {
            if (m_AmbienceSource != null && m_AmbienceLoop != null)
            {
                m_AmbienceSource.clip = m_AmbienceLoop; m_AmbienceSource.loop = true; m_AmbienceSource.volume = m_AmbienceVolume;
                m_AmbienceSource.spatialBlend = 0f; m_AmbienceSource.Play();
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitchJitter = 0f)
        {
            if (clip == null || Instance == null || Instance.m_Pool == null) return;
            var src = Instance.m_Pool.Get();
            src.transform.position = position;
            src.clip = clip; src.volume = volume; src.spatialBlend = 1f;
            src.pitch = 1f + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f);
            src.Play();
            Instance.StartCoroutine(Instance.ReleaseLater(src, clip.length / Mathf.Max(0.1f, src.pitch) + 0.05f));
        }

        public static void PlayUiClick() { if (Instance != null && Instance.m_UiClick != null && Camera.main != null) PlayAt(Instance.m_UiClick, Camera.main.transform.position, 0.6f); }
        public static void PlayTargetDing(Vector3 at) { if (Instance != null) PlayAt(Instance.m_TargetHitDing, at, 0.9f, 0.05f); }
        public static void PlayCountdown(int second)
        {
            if (Instance == null || Instance.m_CountdownClips == null || Instance.m_CountdownClips.Length == 0 || Camera.main == null) return;
            int idx = second <= 0 ? Instance.m_CountdownClips.Length - 1 : Mathf.Clamp(3 - second, 0, Instance.m_CountdownClips.Length - 1);
            PlayAt(Instance.m_CountdownClips[idx], Camera.main.transform.position + Camera.main.transform.forward, 0.7f);
        }
        public static void PlayRoundComplete() { if (Instance != null && Camera.main != null) PlayAt(Instance.m_RoundComplete, Camera.main.transform.position + Camera.main.transform.forward, 0.8f); }
        public static void PlayMenuConfirm() { if (Instance != null && Camera.main != null) PlayAt(Instance.m_MenuConfirm, Camera.main.transform.position + Camera.main.transform.forward, 0.6f); }

        System.Collections.IEnumerator ReleaseLater(AudioSource src, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (src != null) { src.Stop(); m_Pool.Release(src); }
        }
    }
}
