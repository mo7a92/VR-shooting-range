using System;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRRange.Audio
{
    /// <summary>
    /// The jazz radio (a boombox on the weapons counter). Grip it, by hand or with the ray, to switch to the next station;
    /// after the last station it turns off, the next grip turns it on again. A station plays its track and then moves on to the
    /// next one by itself. The sound is a 3D source on the radio, so it is a background murmur at the shooting line and clear
    /// at the counter. The small display shows what is playing; the menu's radio button does the same as a grip.
    /// Music: Kevin MacLeod (incompetech.com), CC BY 4.0 (credited on the display and in ASSET_CREDITS.md).
    /// </summary>
    public class JazzRadio : MonoBehaviour
    {
        [SerializeField] AudioSource m_Source;
        [SerializeField] AudioClip[] m_Tracks = new AudioClip[0];
        [SerializeField] string[] m_Titles = new string[0];
        [SerializeField] TMP_Text m_Display;
        [SerializeField, Range(0f, 1f)] float m_Volume = 0.35f;
        [SerializeField] bool m_PlayOnStart = true;

        int m_Index = -1;
        float m_StartedAt;
        XRSimpleInteractable m_Interactable;
        UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor m_ReleaseNext;

        public static JazzRadio Instance { get; private set; }
        /// <summary>Raised whenever the station changes (or the radio turns on / off).</summary>
        public event Action<JazzRadio> Changed;
        public bool IsOn => m_Index >= 0;
        public int Station => m_Index;
        public int StationCount => m_Tracks != null ? m_Tracks.Length : 0;
        public AudioSource Source => m_Source;
        public string NowPlaying => m_Index >= 0 && m_Titles != null && m_Index < m_Titles.Length ? m_Titles[m_Index] : (m_Index >= 0 && m_Tracks[m_Index] != null ? m_Tracks[m_Index].name : "");

        public void Configure(AudioSource source, AudioClip[] tracks, string[] titles, TMP_Text display)
        {
            m_Source = source; m_Tracks = tracks; m_Titles = titles; m_Display = display;
        }

        void Awake()
        {
            Instance = this;
            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            m_Interactable = GetComponent<XRSimpleInteractable>();
        }

        void OnEnable() { if (m_Interactable != null) m_Interactable.selectEntered.AddListener(OnSelected); }
        void OnDisable() { if (m_Interactable != null) m_Interactable.selectEntered.RemoveListener(OnSelected); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (m_PlayOnStart && StationCount > 0) Play(0); else Refresh();
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            Next();
            // a radio is not carried around: it lets go on the next frame (not inside XRI's own select callback), so the
            // next grip is a fresh press
            m_ReleaseNext = args.interactorObject;
        }

        /// <summary>Next station; after the last one the radio turns off, and from off it starts at the first.</summary>
        public void Next()
        {
            if (StationCount == 0) return;
            int n = m_Index + 1;
            if (n >= StationCount) TurnOff(); else Play(n);
        }

        public void Play(int station)
        {
            if (m_Source == null || StationCount == 0) return;
            m_Index = Mathf.Clamp(station, 0, StationCount - 1);
            m_Source.Stop();
            m_Source.clip = m_Tracks[m_Index]; m_Source.volume = m_Volume; m_Source.loop = false;
            m_Source.Play(); m_StartedAt = Time.unscaledTime;
            AudioManager.PlayUiClick();
            Refresh();
        }

        public void TurnOff()
        {
            m_Index = -1;
            if (m_Source != null) m_Source.Stop();
            AudioManager.PlayUiClick();
            Refresh();
        }

        void Update()
        {
            if (m_ReleaseNext != null)
            {
                var who = m_ReleaseNext; m_ReleaseNext = null;
                if (m_Interactable != null && m_Interactable.interactionManager != null && m_Interactable.interactorsSelecting.Contains(who))
                    m_Interactable.interactionManager.SelectExit(who, m_Interactable);
            }
            // a finished track moves the station on by itself (wrapping round); the grace period covers a streamed clip's start
            if (m_Index >= 0 && m_Source != null && !m_Source.isPlaying && Time.unscaledTime - m_StartedAt > 1f && AudioListener.pause == false)
                Play((m_Index + 1) % StationCount);
        }

        void Refresh()
        {
            if (m_Display != null)
                m_Display.text = IsOn ? $"<b>RADIO</b>  {m_Index + 1}/{StationCount}  {NowPlaying}\n<size=60%>grip: next station / off    music: Kevin MacLeod (incompetech.com) CC BY 4.0</size>"
                                      : "<b>RADIO</b>  off\n<size=60%>grip: turn on    music: Kevin MacLeod (incompetech.com) CC BY 4.0</size>";
            Changed?.Invoke(this);
        }
    }
}
