using UnityEngine;

namespace VRRange.Hands
{
    /// <summary>Finger curl preset (0 = straight, 1 = fully closed) plus thumb spread. Blended at runtime by <see cref="HandPresence"/>.</summary>
    [CreateAssetMenu(menuName = "VR Range/Hand Pose", fileName = "HandPose")]
    public class HandPose : ScriptableObject
    {
        [Range(0f, 1f)] public float thumb = 0.3f;
        [Range(0f, 1f)] public float index = 0.2f;
        [Range(0f, 1f)] public float middle = 0.2f;
        [Range(0f, 1f)] public float ring = 0.25f;
        [Range(0f, 1f)] public float little = 0.3f;
        [Tooltip("0 = thumb alongside the index finger, 1 = thumb spread away from the palm.")]
        [Range(0f, 1f)] public float thumbSpread = 0.4f;
        [Tooltip("When true the index finger follows the trigger value on top of this pose (weapon poses).")]
        public bool indexFollowsTrigger;

        public float Curl(int finger) => finger switch { 0 => thumb, 1 => index, 2 => middle, 3 => ring, _ => little };

        public static HandPose Create(string name, float thumb, float index, float middle, float ring, float little, float spread, bool trigger)
        {
            var p = CreateInstance<HandPose>();
            p.name = name; p.thumb = thumb; p.index = index; p.middle = middle; p.ring = ring; p.little = little; p.thumbSpread = spread; p.indexFollowsTrigger = trigger;
            return p;
        }
    }
}
