using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRRange.Weapons
{
    /// <summary>
    /// Returns a dropped weapon to its counter socket after a delay, or immediately if it falls out of the range.
    /// Keeps the weapon recoverable no matter where the player drops it.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class WeaponHome : MonoBehaviour
    {
        [SerializeField] XRSocketInteractor m_HomeSocket;
        [SerializeField, Range(1f, 60f)] float m_ReturnAfterSeconds = 8f;
        [SerializeField] float m_FloorY = -1f;

        XRGrabInteractable m_Grab;
        Rigidbody m_Body;
        Coroutine m_Timer;

        public XRSocketInteractor HomeSocket { get => m_HomeSocket; set => m_HomeSocket = value; }

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            m_Grab.selectExited.AddListener(OnSelectExited);
            m_Grab.selectEntered.AddListener(OnSelectEntered);
        }

        void OnDisable()
        {
            m_Grab.selectExited.RemoveListener(OnSelectExited);
            m_Grab.selectEntered.RemoveListener(OnSelectEntered);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (m_Timer != null) { StopCoroutine(m_Timer); m_Timer = null; }
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_Timer != null) StopCoroutine(m_Timer);
            m_Timer = StartCoroutine(ReturnCountdown());
        }

        void Update()
        {
            if (transform.position.y < m_FloorY && !m_Grab.isSelected) ReturnHome();
        }

        IEnumerator ReturnCountdown()
        {
            yield return null; // let a hand-to-hand transfer register first
            if (m_Grab.isSelected) yield break;
            yield return new WaitForSeconds(m_ReturnAfterSeconds);
            if (!m_Grab.isSelected) ReturnHome();
        }

        public void ReturnHome()
        {
            if (m_Timer != null) { StopCoroutine(m_Timer); m_Timer = null; }
            if (m_HomeSocket == null) return;
            if (m_Grab.isSelected) return;
            var attach = m_HomeSocket.attachTransform != null ? m_HomeSocket.attachTransform : m_HomeSocket.transform;
            if (m_Body != null) { m_Body.linearVelocity = Vector3.zero; m_Body.angularVelocity = Vector3.zero; }
            transform.SetPositionAndRotation(attach.position, attach.rotation);
            var manager = m_Grab.interactionManager;
            if (manager != null && m_HomeSocket.socketActive)
                manager.SelectEnter((IXRSelectInteractor)m_HomeSocket, (IXRSelectInteractable)m_Grab);
        }
    }
}
