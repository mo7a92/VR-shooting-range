using UnityEngine;

namespace VRRange.Core
{
    public enum GameMode { FreePractice, AccuracyChallenge, MovingTargetChallenge }
    public enum Difficulty { Easy, Medium, Hard }
    public enum TargetMotion { Static, Horizontal, NearFar, Random, PopUp }

    [CreateAssetMenu(menuName = "VR Range/Game Mode Settings", fileName = "GameModeSettings")]
    public class GameModeSettings : ScriptableObject
    {
        [System.Serializable]
        public class DifficultyProfile
        {
            public Difficulty difficulty;
            [Tooltip("Carrier speed in metres per second for horizontal / near-far / random motion.")]
            public float moveSpeed = 0.8f;
            [Tooltip("Seconds a pop-up target stays visible.")]
            public float popUpExposure = 3f;
            [Tooltip("Seconds a pop-up target stays hidden (randomised +/- 30%).")]
            public float popUpHidden = 1.5f;
            [Tooltip("Lateral travel (metres) each side of the lane centre.")]
            public float lateralRange = 0.9f;
            [Tooltip("Score multiplier applied to hits in moving challenge.")]
            public float scoreMultiplier = 1f;
        }

        [Header("Accuracy Challenge")]
        [Range(5, 100)] public int accuracyChallengeShots = 20;
        public float accuracyChallengeDistance = 10f;

        [Header("Moving Target Challenge")]
        [Range(15, 300)] public int movingChallengeSeconds = 60;
        public TargetMotion movingChallengeMotion = TargetMotion.Random;

        [Header("Lanes")]
        public float[] distancePresets = { 5f, 10f, 15f, 25f };
        public float defaultDistance = 10f;

        [Header("Difficulty")]
        public DifficultyProfile[] profiles =
        {
            new DifficultyProfile { difficulty = Difficulty.Easy,   moveSpeed = 0.6f, popUpExposure = 4f, popUpHidden = 1.5f, lateralRange = 0.8f, scoreMultiplier = 1f },
            new DifficultyProfile { difficulty = Difficulty.Medium, moveSpeed = 1.2f, popUpExposure = 2.5f, popUpHidden = 1.2f, lateralRange = 1.0f, scoreMultiplier = 1.5f },
            new DifficultyProfile { difficulty = Difficulty.Hard,   moveSpeed = 2.0f, popUpExposure = 1.5f, popUpHidden = 1.0f, lateralRange = 1.2f, scoreMultiplier = 2f },
        };

        public DifficultyProfile Profile(Difficulty d)
        {
            if (profiles != null)
                foreach (var p in profiles) if (p != null && p.difficulty == d) return p;
            return new DifficultyProfile { difficulty = d };
        }

        public float ClampDistance(float d)
        {
            if (distancePresets == null || distancePresets.Length == 0) return d;
            float best = distancePresets[0];
            foreach (var p in distancePresets) if (Mathf.Abs(p - d) < Mathf.Abs(best - d)) best = p;
            return best;
        }
    }
}
