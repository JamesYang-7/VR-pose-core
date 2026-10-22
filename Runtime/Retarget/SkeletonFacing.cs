using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// A body's canonical frame: the rig's own up axis, plus the forward it faces, read from the
    /// hips (right runs left hip → right hip; Unity is left-handed, so <c>Cross(right, up)</c> is
    /// forward). The up axis is given, not measured, because joint positions carry the template's
    /// lean (SMPL's pelvis→neck line tilts a few degrees), and that lean is joint placement, not
    /// posture — only the yaw is read from geometry.
    /// </summary>
    public static class SkeletonFacing
    {
        /// <summary>Rotation whose +Y is <paramref name="up"/> and +Z the horizontal forward.</summary>
        public static Quaternion FromJoints(Vector3 up, Vector3 leftHip, Vector3 rightHip)
        {
            var forward = Vector3.Cross(rightHip - leftHip, up);
            return Quaternion.LookRotation(forward, up);
        }
    }
}
