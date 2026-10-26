using UnityEngine;

namespace VRRange.Core
{
    /// <summary>Ring radii (metres from target centre) and the points awarded inside each ring.</summary>
    [CreateAssetMenu(menuName = "VR Range/Scoring Rules", fileName = "ScoringRules")]
    public class ScoringRules : ScriptableObject
    {
        [System.Serializable]
        public struct Ring
        {
            public string name;
            [Tooltip("Hits closer than this radius (metres) score this ring unless a smaller ring matched.")]
            public float radius;
            public int points;
        }

        [Tooltip("Ordered from the smallest radius (bullseye) outwards.")]
        public Ring[] rings =
        {
            new Ring { name = "Bullseye", radius = 0.05f, points = 10 },
            new Ring { name = "Inner",    radius = 0.12f, points = 8 },
            new Ring { name = "Middle",   radius = 0.20f, points = 5 },
            new Ring { name = "Outer",    radius = 0.28f, points = 2 },
        };

        public const int MissIndex = -1;

        /// <summary>Returns the ring index for a hit at the given distance from centre, or MissIndex (points 0).</summary>
        public int RingIndexFor(float distanceFromCentre)
        {
            if (rings == null) return MissIndex;
            for (int i = 0; i < rings.Length; i++)
                if (distanceFromCentre <= rings[i].radius) return i;
            return MissIndex;
        }

        public int PointsFor(int ringIndex) => ringIndex >= 0 && rings != null && ringIndex < rings.Length ? rings[ringIndex].points : 0;
        public string NameFor(int ringIndex) => ringIndex >= 0 && rings != null && ringIndex < rings.Length ? rings[ringIndex].name : "Miss";
        public int MaxPoints => rings != null && rings.Length > 0 ? rings[0].points : 0;
    }
}
