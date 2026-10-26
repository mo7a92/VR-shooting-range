using UnityEngine;

namespace VRRange.Targets
{
    public struct HitInfo
    {
        public Vector3 point;
        public Vector3 normal;
        public Collider collider;
        public float travelDistance;
        public float travelTime;
        public Weapons.Weapon weapon;
    }

    public interface IHitReceiver
    {
        /// <summary>Called once per projectile at most (projectiles consume themselves on first hit).</summary>
        void OnProjectileHit(in HitInfo hit);
    }
}
