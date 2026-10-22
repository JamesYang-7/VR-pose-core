using System;
using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// The layouts the pose models share (RPM-README §2): the 54-float input frame <see cref="GorpInputProcessor"/>
    /// builds from the tracked head and hands, and the 198-float motion row a step returns, 22 × 6D rotations (local,
    /// root global) then 22 world joint positions, SMPL Z-up. Also the standing pose the live runners seed from.
    /// </summary>
    public static class PoseLayout
    {
        /// <summary>Floats per input frame (RPM-README §2).</summary>
        public const int InputWidth = 54;

        /// <summary>Floats per motion row: 22 × 6D rotations, then 22 × 3 world positions.</summary>
        public const int MotionWidth = 198;

        /// <summary>The rotation part of a motion row.</summary>
        public const int RotationWidth = 132;

        /// <summary>SMPL-X body joints.</summary>
        public const int Joints = 22;

        /// <summary>The head joint, which the models place on the tracked head.</summary>
        public const int HeadJoint = 15;

        static readonly float[] kIdentity6D = { 1, 0, 0, 0, 1, 0 };

        /// <summary>
        /// The rotations of the standing pose the live runners seed from: every joint at identity except the root,
        /// <c>Rz(yaw_head + 90°) · Rx(90°)</c> with <c>yaw_head</c> from <paramref name="frame"/>'s head rotation.
        /// Writes the 132 rotation floats of <paramref name="dst"/>.
        /// </summary>
        public static void StandingRotations(float[] frame, float[] dst)
        {
            // Head rotation's third column = a × b of its 6D columns (sixd_to_matrot, not orthonormalised).
            var a = new Vector3(frame[0], frame[1], frame[2]);
            var b = new Vector3(frame[3], frame[4], frame[5]);
            var c = Vector3.Cross(a, b);
            double yaw = Math.Atan2(c.y, c.x) + Math.PI / 2; // SMPL forward is local +z
            float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
            // Rx(90°) = [[1,0,0],[0,0,-1],[0,1,0]] (column-vector matrices)
            for (int j = 0; j < Joints; j++) Array.Copy(kIdentity6D, 0, dst, j * 6, 6);
            dst[0] = cy; dst[1] = sy; dst[2] = 0f; // first column
            dst[3] = 0f; dst[4] = 0f; dst[5] = 1f; // second column
        }
    }
}
