using System;
using System.Collections.Generic;
using UnityEngine;
using VRRange.Audio;
using VRRange.Ballistics;
using VRRange.Core;
using VRRange.Scoring;

namespace VRRange.Targets
{
    /// <summary>
    /// Paper/steel bullseye face. Scores a hit by its distance from the face centre using <see cref="ScoringRules"/>,
    /// places an impact marker and forwards the result to the <see cref="ScoreManager"/>.
    /// </summary>
    public class BullseyeTarget : MonoBehaviour, IHitReceiver
    {
        [SerializeField] ScoringRules m_Rules;
        [SerializeField] Transform m_Face;            // local XY plane, +Z faces the shooter
        [SerializeField] GameObject m_MarkerPrefab;   // small quad placed at hit point
        [SerializeField, Range(0, 200)] int m_MaxMarkers = 60;
        [SerializeField] bool m_Scoring = true;

        readonly List<GameObject> m_Markers = new List<GameObject>();
        int m_HitsSinceReset;

        public event Action<BullseyeTarget, HitResult> Hit;

        public ScoringRules Rules => m_Rules;
        public Transform Face => m_Face != null ? m_Face : transform;
        public int HitsSinceReset => m_HitsSinceReset;
        public bool ScoringEnabled { get => m_Scoring; set => m_Scoring = value; }
        public float DistanceFromShooter { get; set; }

        public void Initialize(ScoringRules rules, Transform face, GameObject markerPrefab) { m_Rules = rules; m_Face = face; m_MarkerPrefab = markerPrefab; }

        public void OnProjectileHit(in HitInfo hit)
        {
            var face = Face;
            Vector3 local = face.InverseTransformPoint(hit.point);
            float r = new Vector2(local.x, local.y).magnitude;
            int ring = m_Rules != null ? m_Rules.RingIndexFor(r) : ScoringRules.MissIndex;
            var result = new HitResult
            {
                ringIndex = ring,
                ringName = m_Rules != null ? m_Rules.NameFor(ring) : "Hit",
                points = m_Rules != null ? m_Rules.PointsFor(ring) : 0,
                distance = hit.travelDistance,
                worldPoint = hit.point
            };
            m_HitsSinceReset++;
            PlaceMarker(hit.point, hit.normal);
            if (result.points > 0) AudioManager.PlayTargetDing(hit.point);
            if (m_Scoring) ScoreManager.NotifyHit(result);
            Hit?.Invoke(this, result);
        }

        void PlaceMarker(Vector3 point, Vector3 normal)
        {
            if (m_MarkerPrefab == null) return;
            GameObject marker;
            if (m_Markers.Count >= m_MaxMarkers)
            {
                marker = m_Markers[0]; m_Markers.RemoveAt(0); m_Markers.Add(marker);
            }
            else
            {
                marker = Instantiate(m_MarkerPrefab, Face);
                m_Markers.Add(marker);
            }
            marker.transform.SetPositionAndRotation(point + normal * 0.003f, Quaternion.LookRotation(-normal));
            marker.SetActive(true);
        }

        public void ResetTarget()
        {
            m_HitsSinceReset = 0;
            foreach (var m in m_Markers) if (m != null) m.SetActive(false);
            ImpactEffects.Instance?.ClearDecalsUnder(transform);
        }
    }
}
