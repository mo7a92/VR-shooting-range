using UnityEngine;

namespace VRRange.Hands
{
    public enum GripRole { Primary, Support }

    /// <summary>
    /// Where a hand belongs on a weapon. The anchor's transform is the palm pose: position = palm centre on the grip,
    /// forward (+Z) = direction the proximal finger segments point, up (+Y) = back of the hand (away from the grip).
    /// </summary>
    public class WeaponGripAnchor : MonoBehaviour
    {
        public GripRole role = GripRole.Primary;
        [Tooltip("Pose used by the hand while holding this anchor.")]
        public HandPose pose;
        [Tooltip("Mirror the palm pose across the weapon's local X axis for the other hand (default anchor is authored for the right hand).")]
        public bool authoredForRightHand = true;

        /// <summary>World palm pose for the given hand. Left hands get a mirrored anchor unless the anchor was authored for them.</summary>
        public Pose GetPalmPose(bool isLeftHand)
        {
            if (isLeftHand == !authoredForRightHand) return new Pose(transform.position, transform.rotation);
            var parent = transform.parent != null ? transform.parent : transform;
            var lp = parent.InverseTransformPoint(transform.position); lp.x = -lp.x;
            var lr = Quaternion.Inverse(parent.rotation) * transform.rotation;
            // mirror rotation across the X plane: flip the x components of the forward/up axes
            var f = lr * Vector3.forward; var u = lr * Vector3.up;
            f.x = -f.x; u.x = -u.x;
            var mirrored = Quaternion.LookRotation(f, u);
            return new Pose(parent.TransformPoint(lp), parent.rotation * mirrored);
        }
    }
}
