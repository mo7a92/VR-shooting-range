using UnityEngine;

namespace VRRange.Core
{
    public enum SurfaceType { Concrete, Metal, Paper, Wood, Rubber, Generic }

    /// <summary>Tells the impact system what a collider is made of (decal, particle and sound choice).</summary>
    public class SurfaceMaterialTag : MonoBehaviour
    {
        public SurfaceType surface = SurfaceType.Generic;
    }
}
