using System.Collections.Generic;
using UnityEngine;

namespace VRRange.Weapons
{
    /// <summary>
    /// Hand-contact volumes of a weapon: boxes fitted to the visual mesh per part (grip, frame, slide, handguard, ...) by
    /// the prefab builder, on the HandContact layer (collides with nothing; projectiles and grab casters ignore it).
    /// <see cref="Hands.HandPresence"/> keeps palms and fingers out of them and closes the fingers onto them.
    /// </summary>
    public class WeaponContactVolumes : MonoBehaviour
    {
        public List<Collider> volumes = new List<Collider>();

        /// <summary>Largest depth (metres) of a point inside any volume; 0 when outside all of them.</summary>
        public float Depth(Vector3 point)
        {
            float depth = 0f;
            foreach (var c in volumes)
            {
                if (c == null) continue;
                float sd = Hands.HandPresence.SignedDistance(c, point);
                if (-sd > depth) depth = -sd;
            }
            return depth;
        }

        /// <summary>Snapshot of the volumes as plain shapes for bulk queries (tests, hand solver).</summary>
        public List<Hands.HandPresence.ContactShape> Shapes()
        {
            var list = new List<Hands.HandPresence.ContactShape>();
            foreach (var c in volumes) if (c != null) list.Add(Hands.HandPresence.ContactShape.From(c));
            return list;
        }

        /// <summary>Smallest signed distance from a point to the volumes' surfaces (negative inside).</summary>
        public float SignedDistance(Vector3 point)
        {
            float d = float.MaxValue;
            foreach (var c in volumes) { if (c == null) continue; float sd = Hands.HandPresence.SignedDistance(c, point); if (sd < d) d = sd; }
            return d == float.MaxValue ? 0f : d;
        }
    }
}
