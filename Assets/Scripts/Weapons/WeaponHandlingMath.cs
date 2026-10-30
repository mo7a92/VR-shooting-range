using UnityEngine;

namespace VRRange.Weapons
{
    /// <summary>Pure math used by <see cref="WeaponHandlingTransformer"/>; kept static so it can be unit-tested.</summary>
    public static class WeaponHandlingMath
    {
        /// <summary>Weapon world pose that places its grip anchor (weapon-local pose) exactly on the hand pose.</summary>
        public static Pose PoseFromGrip(Pose handWorld, Pose gripLocal)
        {
            var rot = handWorld.rotation * Quaternion.Inverse(gripLocal.rotation);
            var pos = handWorld.position - rot * gripLocal.position;
            return new Pose(pos, rot);
        }

        /// <summary>
        /// Two-point aiming: rotate the one-handed pose so the weapon-local vector from the primary grip to the support grip
        /// points at the support hand, keeping the roll given by the primary hand. influence 0 = ignore support hand.
        /// </summary>
        public static Quaternion TwoPointAim(Quaternion oneHandRotation, Vector3 primaryHandPos, Vector3 supportHandPos, Vector3 gripToSupportLocal, float influence)
        {
            if (influence <= 0f || gripToSupportLocal.sqrMagnitude < 1e-8f) return oneHandRotation;
            var toSupport = supportHandPos - primaryHandPos;
            if (toSupport.sqrMagnitude < 1e-6f) return oneHandRotation;
            var predicted = oneHandRotation * gripToSupportLocal;
            var delta = Quaternion.FromToRotation(predicted.normalized, toSupport.normalized);
            var full = delta * oneHandRotation;
            return influence >= 1f ? full : Quaternion.Slerp(oneHandRotation, full, influence);
        }

        /// <summary>Exponential approach factor for a first-order lag with the given time constant.</summary>
        public static float LagFactor(float deltaTime, float responseSeconds) => responseSeconds <= 1e-5f ? 1f : 1f - Mathf.Exp(-deltaTime / responseSeconds);

        /// <summary>Clamps a lagged position so it never trails the target by more than maxLag.</summary>
        public static Vector3 ClampLag(Vector3 target, Vector3 lagged, float maxLag)
        {
            var d = lagged - target; float m = d.magnitude;
            return m > maxLag ? target + d * (maxLag / m) : lagged;
        }

        /// <summary>Clamps a lagged rotation so it never differs from the target by more than maxDegrees.</summary>
        public static Quaternion ClampLag(Quaternion target, Quaternion lagged, float maxDegrees)
        {
            float a = Quaternion.Angle(target, lagged);
            return a > maxDegrees ? Quaternion.Slerp(target, lagged, maxDegrees / a) : lagged;
        }

        /// <summary>Weight-induced muzzle drop (degrees) for the current grip blend (0 = one hand, 1 = two hands).</summary>
        public static float Sag(float oneHandSag, float twoHandSag, float twoHandBlend) => Mathf.Lerp(oneHandSag, twoHandSag, Mathf.Clamp01(twoHandBlend));

        /// <summary>Sway amplitude (degrees) including the instability bonus after shots.</summary>
        public static float SwayAmplitude(float oneHandSway, float twoHandSway, float twoHandBlend, float instability) =>
            Mathf.Lerp(oneHandSway, twoHandSway, Mathf.Clamp01(twoHandBlend)) * (1f + 2f * Mathf.Clamp01(instability));

        /// <summary>Recoil scale for the current grip state and weapon mass relative to a 1 kg reference.</summary>
        public static float RecoilScale(float unsupportedMultiplier, float supportedMultiplier, float twoHandBlend, float virtualMassKg)
        {
            float grip = Mathf.Lerp(unsupportedMultiplier, supportedMultiplier, Mathf.Clamp01(twoHandBlend));
            float mass = Mathf.Clamp(1f / Mathf.Sqrt(Mathf.Max(0.2f, virtualMassKg)), 0.45f, 1.6f); // heavier weapons kick less per shot
            return grip * mass;
        }

        /// <summary>Recovery speed for the current grip state.</summary>
        public static float RecoverySpeed(float unsupported, float supported, float twoHandBlend) => Mathf.Lerp(unsupported, supported, Mathf.Clamp01(twoHandBlend));

        /// <summary>Deterministic 2D sway offset (degrees pitch, yaw) from Perlin noise.</summary>
        public static Vector2 Sway(float time, float frequencyHz, float amplitudeDegrees, float seed)
        {
            float t = time * frequencyHz;
            float pitch = (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f;
            float yaw = (Mathf.PerlinNoise(seed + 17.3f, t * 0.83f) - 0.5f) * 2f;
            return new Vector2(pitch, yaw) * amplitudeDegrees;
        }
    }
}
