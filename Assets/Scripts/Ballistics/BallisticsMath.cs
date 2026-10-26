using UnityEngine;

namespace VRRange.Ballistics
{
    /// <summary>Pure functions for the game-style projectile model (unit-testable, no Unity objects needed).</summary>
    public static class BallisticsMath
    {
        /// <summary>Semi-implicit Euler step under constant gravity.</summary>
        public static void Integrate(ref Vector3 position, ref Vector3 velocity, float deltaTime, Vector3 gravity)
        {
            velocity += gravity * deltaTime;
            position += velocity * deltaTime;
        }

        /// <summary>Time for a projectile launched horizontally at speed to cover a horizontal distance (ignores drag).</summary>
        public static float TimeOfFlight(float distance, float speed) => speed <= 0f ? float.PositiveInfinity : distance / speed;

        /// <summary>Vertical drop after flying a horizontal distance at the given speed under gravity magnitude g.</summary>
        public static float DropAtDistance(float distance, float speed, float g)
        {
            float t = TimeOfFlight(distance, speed);
            return 0.5f * g * t * t;
        }

        /// <summary>Upward aim angle (degrees) needed so the shot lands on a target at range (small angle, flat trajectory).</summary>
        public static float CompensationAngleDegrees(float distance, float speed, float g)
        {
            if (speed <= 0f || distance <= 0f) return 0f;
            float s = g * distance / (speed * speed); // sin(2a)
            s = Mathf.Clamp(s, -1f, 1f);
            return 0.5f * Mathf.Asin(s) * Mathf.Rad2Deg;
        }

        /// <summary>Rotates a direction by a random cone of the given half-angle using the supplied random values in [0,1).</summary>
        public static Vector3 ApplySpread(Vector3 direction, float halfAngleDegrees, float random01A, float random01B)
        {
            if (halfAngleDegrees <= 0f) return direction.normalized;
            float angle = halfAngleDegrees * Mathf.Sqrt(random01A); // uniform over the disc
            float azimuth = random01B * 360f;
            var rot = Quaternion.AngleAxis(azimuth, direction) * Quaternion.AngleAxis(angle, Vector3.Cross(direction, Vector3.up).sqrMagnitude > 1e-6f ? Vector3.Cross(direction, Vector3.up) : Vector3.right);
            return (rot * direction).normalized;
        }
    }
}
