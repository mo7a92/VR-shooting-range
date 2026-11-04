using TMPro;
using UnityEngine;
using VRRange.Core;

namespace VRRange.Weapons
{
    /// <summary>
    /// Small world-space ammo counter on the weapon itself (rear of the receiver / slide): "12 / 12  SEMI". Turns red when
    /// the magazine is empty and dims while reloading. Driven purely by <see cref="Weapon"/> events; no per-frame work.
    /// </summary>
    public class WeaponAmmoDisplay : MonoBehaviour
    {
        [SerializeField] Weapon m_Weapon;
        [SerializeField] TMP_Text m_Text;
        [SerializeField] Color m_NormalColor = new Color(1f, 0.72f, 0.2f, 1f);
        [SerializeField] Color m_LowColor = new Color(1f, 0.45f, 0.15f, 1f);
        [SerializeField] Color m_EmptyColor = new Color(1f, 0.2f, 0.15f, 1f);
        [SerializeField] Color m_ReloadingColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        public void Configure(Weapon weapon, TMP_Text text) { m_Weapon = weapon; m_Text = text; }

        void OnEnable()
        {
            if (m_Weapon == null) m_Weapon = GetComponentInParent<Weapon>();
            if (m_Text == null) m_Text = GetComponentInChildren<TMP_Text>(true);
            if (m_Weapon == null) return;
            m_Weapon.AmmoChanged += OnAmmo; m_Weapon.FireModeChanged += OnMode; m_Weapon.ReloadStateChanged += OnReload;
            Refresh();
        }

        void Start() => Refresh();

        void OnDisable()
        {
            if (m_Weapon == null) return;
            m_Weapon.AmmoChanged -= OnAmmo; m_Weapon.FireModeChanged -= OnMode; m_Weapon.ReloadStateChanged -= OnReload;
        }

        void OnAmmo(Weapon w, int rounds, int mag) => Refresh();
        void OnMode(Weapon w, FireMode mode) => Refresh();
        void OnReload(Weapon w, bool reloading) => Refresh();

        public void Refresh()
        {
            if (m_Text == null || m_Weapon == null) return;
            int rounds = m_Weapon.Rounds, mag = m_Weapon.MagazineSize;
            string mode = m_Weapon.CurrentFireMode == FireMode.Auto ? "AUTO" : "SEMI";
            m_Text.text = m_Weapon.IsReloading ? $"-- / {mag}  {mode}" : $"{rounds} / {mag}  {mode}";
            m_Text.color = m_Weapon.IsReloading ? m_ReloadingColor : rounds <= 0 ? m_EmptyColor : rounds <= Mathf.Max(2, mag / 5) ? m_LowColor : m_NormalColor;
        }
    }
}
