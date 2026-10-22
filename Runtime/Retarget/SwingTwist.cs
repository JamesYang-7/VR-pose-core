using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// Swing–twist decomposition helpers. A rotation <c>q</c> splits into <c>q = swing * twist</c>,
    /// where <c>twist</c> spins about a given axis (applied first, in the rotated frame's own
    /// coordinates) and <c>swing</c> moves that axis. Used to read how far a limb bone has rolled
    /// about its own length, which the twist bones then share out along the segment.
    /// </summary>
    public static class SwingTwist
    {
        /// <summary>
        /// Signed angle, in degrees within (-180, 180], of the twist component of <paramref name="q"/>
        /// about <paramref name="axis"/> (right-hand sense of <see cref="Quaternion.AngleAxis"/>).
        /// Returns 0 when <paramref name="q"/> is a pure 180° swing, where the twist is undefined.
        /// </summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            var a = axis.normalized;
            float p = Vector3.Dot(new Vector3(q.x, q.y, q.z), a);
            // The twist is the normalized quaternion (p * a, w); its angle is 2 * atan2(p, w).
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(p, q.w) * Mathf.Rad2Deg);
        }
    }
}
