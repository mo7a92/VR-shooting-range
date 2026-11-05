using UnityEngine;
using UnityEngine.Rendering.Universal;
using VRRange.Core;

namespace VRRange.Weapons
{
    /// <summary>
    /// Working riflescope: a narrow-field camera at the objective renders into a texture shown on the eyepiece lens
    /// (shader VRRange/ScopeLens: field stop, reticle, exit-pupil shadow). The line of sight is zeroed so it crosses the
    /// bullet's path (muzzle velocity and gravity from the weapon definition) at <see cref="ZeroMetres"/>. The camera only
    /// renders while a hand holds the weapon and an eye is near the eyepiece; otherwise the lens is dark glass.
    /// </summary>
    public class WeaponScope : MonoBehaviour
    {
        [SerializeField] Weapon m_Weapon;
        [SerializeField] Camera m_Camera;
        [SerializeField] Renderer m_Lens;
        [Tooltip("True field of view in degrees (PU 3.5x scope: about 4.5 deg).")]
        [SerializeField, Range(1f, 20f)] float m_FieldOfView = 4.5f;
        [SerializeField, Range(5f, 300f)] float m_ZeroMetres = 25f;
        [SerializeField, Range(128, 2048)] int m_Resolution = 640;
        [Tooltip("The scope renders only while an eye (main camera) is within this distance of the eyepiece.")]
        [SerializeField, Range(0.1f, 2f)] float m_ActiveDistance = 0.45f;

        RenderTexture m_Texture;
        MaterialPropertyBlock m_Block;
        bool m_Active;
        static readonly int s_ScopeTex = Shader.PropertyToID("_ScopeTex"), s_ActiveId = Shader.PropertyToID("_Active");

        public Camera ScopeCamera => m_Camera;
        public Renderer Lens => m_Lens;
        public float FieldOfView => m_FieldOfView;
        public float ZeroMetres => m_ZeroMetres;
        public bool IsActive => m_Active;
        public RenderTexture Texture => m_Texture;
        /// <summary>Test hook: render even with no eye near the eyepiece.</summary>
        public bool ForceActive { get; set; }

        public void Configure(Weapon weapon, Camera cam, Renderer lens, float fieldOfView, float zeroMetres)
        {
            m_Weapon = weapon; m_Camera = cam; m_Lens = lens; m_FieldOfView = fieldOfView; m_ZeroMetres = zeroMetres;
        }

        void Awake()
        {
            if (m_Weapon == null) m_Weapon = GetComponentInParent<Weapon>();
            m_Block = new MaterialPropertyBlock();
            if (m_Camera == null) return;
            m_Camera.enabled = false;
            m_Camera.fieldOfView = m_FieldOfView;
            m_Camera.nearClipPlane = 0.05f; m_Camera.farClipPlane = 200f;
            m_Camera.stereoTargetEye = StereoTargetEyeMask.None;
            // the weapon itself (front sight, barrel) and the hand-contact helpers never show in the image
            m_Camera.cullingMask &= ~(Mask("Weapon") | Mask("HeadsetOnly") | Mask("HandContact"));
            var data = m_Camera.GetUniversalAdditionalCameraData();
            if (data != null) { data.allowXRRendering = false; data.renderShadows = false; data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.None; data.requiresDepthOption = CameraOverrideOption.Off; }
            Zero();
        }

        static int Mask(string layer) { int l = LayerMask.NameToLayer(layer); return l >= 0 ? 1 << l : 0; }

        /// <summary>
        /// Aims the scope camera so its axis crosses the bullet's path at the zero distance (bore along the muzzle's forward,
        /// drop from gravity at the definition's muzzle velocity).
        /// </summary>
        public void Zero()
        {
            if (m_Camera == null || m_Weapon == null || m_Weapon.Muzzle == null) return;
            var root = m_Weapon.transform; var muzzle = m_Weapon.Muzzle;
            var def = m_Weapon.Definition;
            float v = def != null ? def.muzzleVelocity : 800f, g = def != null ? def.gravityMultiplier * Physics.gravity.magnitude : 9.81f;
            var camLocal = root.InverseTransformPoint(m_Camera.transform.position);
            var mLocal = root.InverseTransformPoint(muzzle.position); var mFwd = root.InverseTransformDirection(muzzle.forward);
            float along = m_ZeroMetres - (mLocal.z - camLocal.z);
            float t = along / Mathf.Max(1f, v);
            // zeroed with the weapon level (wherever it happens to rest when this runs): drop is along the weapon's own down
            var target = mLocal + mFwd * along + Vector3.down * (0.5f * g * t * t);
            var rotLocal = Quaternion.LookRotation(target - camLocal, Vector3.up);
            m_Camera.transform.rotation = root.rotation * rotLocal;
        }

        void OnEnable() { SetActive(false); }

        void OnDisable()
        {
            SetActive(false);
            if (m_Texture != null) { m_Texture.Release(); Destroy(m_Texture); m_Texture = null; }
        }

        void LateUpdate()
        {
            bool want = m_Camera != null && m_Weapon != null && (ForceActive || (m_Weapon.IsHeld && EyeNear()));
            if (want != m_Active) SetActive(want);
        }

        bool EyeNear()
        {
            var main = Camera.main;
            return main != null && m_Lens != null && (main.transform.position - m_Lens.transform.position).sqrMagnitude < m_ActiveDistance * m_ActiveDistance;
        }

        void SetActive(bool on)
        {
            m_Active = on;
            if (on && m_Texture == null)
            {
                m_Texture = new RenderTexture(m_Resolution, m_Resolution, 24, RenderTextureFormat.ARGB32) { name = "ScopeView", antiAliasing = 1, useMipMap = false };
                m_Texture.Create();
            }
            if (m_Camera != null) { m_Camera.targetTexture = on ? m_Texture : null; m_Camera.enabled = on; }
            if (m_Lens != null)
            {
                m_Lens.GetPropertyBlock(m_Block);
                if (m_Texture != null) m_Block.SetTexture(s_ScopeTex, m_Texture);
                m_Block.SetFloat(s_ActiveId, on ? 1f : 0f);
                m_Lens.SetPropertyBlock(m_Block);
            }
        }
    }
}
