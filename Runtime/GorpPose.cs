using MyUtils;
using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// Puts a model's pose (SMPL-X joint rotations, local with the root global, in the right-handed Z-up
    /// SMPL world; column-vector matrices) onto the Unity SMPL-X rig (<c>smplx-visemes-unity.fbx</c>, bones
    /// found by <see cref="AnimUtils.BindBones"/>).
    ///
    /// <para>Each rotation R becomes F·R·F with F = diag(−1, 1, 1): the rig's bone frames are the SMPL
    /// template's, mirrored across x by the FBX import, so this is the handedness change. The root then gets
    /// the Z-up → Y-up fix inside <see cref="AnimUtils.SetPose(ref Transform[], ref Quaternion[], int)"/>,
    /// and world positions map as <see cref="MathUtils.BlenderToUnity"/>, (x, y, z) → (−x, z, −y). Checked on
    /// the bundle sequences: posed this way, every bone points where the motion row's own joint positions
    /// say, to 2.2° mean / 6° worst (the residual is the subject's skeleton vs the rig's, constant over
    /// frames).</para>
    /// </summary>
    public static class GorpPose
    {
        /// <summary>SMPL Z-up world position → Unity world position.</summary>
        public static Vector3 ToUnity(Vector3 smpl) => MathUtils.BlenderToUnity(smpl);

        /// <summary>
        /// Rig-local rotations from the 6D part of a motion row starting at <paramref name="offset"/> in
        /// <paramref name="motion"/>. The 6D is orthonormalised (Gram–Schmidt) for display only; never feed
        /// the result back into the model's history.
        /// </summary>
        public static void RotationsFromSixD(float[] motion, int offset, Quaternion[] dst)
        {
            for (int j = 0; j < PoseLayout.Joints; j++)
            {
                int o = offset + j * 6;
                var a = new Vector3(motion[o], motion[o + 1], motion[o + 2]).normalized;
                var b = new Vector3(motion[o + 3], motion[o + 4], motion[o + 5]);
                b = (b - Vector3.Dot(a, b) * a).normalized;
                dst[j] = FromColumns(a, b, Vector3.Cross(a, b));
            }
        }

        /// <summary>Rig-local rotations from <c>joint_rotmat</c>: 22 row-major 3×3 matrices.</summary>
        public static void RotationsFromMatrices(float[] rotmat, Quaternion[] dst)
        {
            for (int j = 0; j < PoseLayout.Joints; j++)
            {
                int o = j * 9;
                dst[j] = FromColumns(new Vector3(rotmat[o], rotmat[o + 3], rotmat[o + 6]),
                                     new Vector3(rotmat[o + 1], rotmat[o + 4], rotmat[o + 7]),
                                     new Vector3(rotmat[o + 2], rotmat[o + 5], rotmat[o + 8]));
            }
        }

        /// <summary>
        /// Poses <paramref name="bones"/> with <paramref name="localRotations"/> (from the methods above) and
        /// translates the rig's root bone (<c>pelvis.parent</c>) so the pelvis lands on
        /// <paramref name="pelvisWorld"/> (Unity world).
        /// </summary>
        public static void Apply(Transform[] bones, Quaternion[] localRotations, Vector3 pelvisWorld)
        {
            AnimUtils.SetPose(ref bones, ref localRotations);
            var root = bones[0].parent;
            root.position += pelvisWorld - bones[0].position;
        }

        /// <summary>
        /// The rig's own rest skeleton as the step graph's <c>joints_rest</c>: 22 × 3 floats in the SMPL template
        /// frame (Y up, +z forward, +x the body's left, metres), relative to the rig's root bone. Puts joints
        /// 1–21 at rest (rig-local identity, which is F·I·F), reads every joint in the pelvis's own frame (the
        /// template's axes mirrored across x, whatever the parents' rotations), and restores the pose.
        /// </summary>
        public static float[] RestJoints(Transform[] bones)
        {
            var saved = new Quaternion[bones.Length];
            for (int j = 0; j < bones.Length; j++) saved[j] = bones[j].localRotation;
            for (int j = 1; j < PoseLayout.Joints; j++) bones[j].localRotation = Quaternion.identity;

            var toPelvis = Quaternion.Inverse(bones[0].rotation);
            var origin = bones[0].parent.position;
            var joints = new float[PoseLayout.Joints * 3];
            for (int j = 0; j < PoseLayout.Joints; j++)
            {
                var v = toPelvis * (bones[j].position - origin);
                joints[j * 3] = -v.x;
                joints[j * 3 + 1] = v.y;
                joints[j * 3 + 2] = v.z;
            }

            for (int j = 0; j < bones.Length; j++) bones[j].localRotation = saved[j];
            return joints;
        }

        // R given by its columns (SMPL, right-handed) → the rig's left-handed rotation F·R·F.
        static Quaternion FromColumns(Vector3 c0, Vector3 c1, Vector3 c2)
        {
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(c0.x, -c0.y, -c0.z, 0f));
            m.SetColumn(1, new Vector4(-c1.x, c1.y, c1.z, 0f));
            m.SetColumn(2, new Vector4(-c2.x, c2.y, c2.z, 0f));
            return m.rotation;
        }
    }
}
