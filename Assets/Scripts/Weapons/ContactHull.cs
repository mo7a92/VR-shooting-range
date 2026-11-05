using System.Collections.Generic;
using UnityEngine;

namespace VRRange.Weapons
{
    /// <summary>
    /// Convex hand-contact shape of one weapon part: a 26-direction discrete oriented polytope (the faces, edges and
    /// corners of a cube) fitted to the part's mesh vertices by the prefab builder, in this transform's local frame.
    /// It follows a raked grip, a rounded front strap or a tapered handguard, where a single box sticks out past the
    /// visible surface (the rifle grip box was 71 mm deep around a 50 mm grip). Lives next to the part's BoxCollider,
    /// which stays as the coarse volume; <see cref="Hands.HandPresence.ContactShape"/> uses the hull when present.
    /// </summary>
    public class ContactHull : MonoBehaviour
    {
        public Vector3[] normals;
        public float[] distances;

        static Vector3[] s_Directions;
        /// <summary>The 26 unit directions: 6 faces, 12 edges, 8 corners of a cube.</summary>
        public static Vector3[] Directions
        {
            get
            {
                if (s_Directions != null) return s_Directions;
                var list = new List<Vector3>();
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                            if (x != 0 || y != 0 || z != 0) list.Add(new Vector3(x, y, z).normalized);
                s_Directions = list.ToArray();
                return s_Directions;
            }
        }

        /// <summary>Fits the hull to points given in this transform's local frame.</summary>
        public void Fit(IReadOnlyList<Vector3> localPoints)
        {
            var dirs = Directions;
            normals = (Vector3[])dirs.Clone();
            distances = new float[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                float d = float.NegativeInfinity;
                for (int k = 0; k < localPoints.Count; k++) { float v = Vector3.Dot(dirs[i], localPoints[k]); if (v > d) d = v; }
                distances[i] = d;
            }
        }

        public bool IsValid => normals != null && distances != null && normals.Length == distances.Length && normals.Length > 0;
    }
}
