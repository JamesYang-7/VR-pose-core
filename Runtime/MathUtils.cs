using UnityEngine;

namespace MyUtils
{
    public static class MathUtils
    {
        public static Quaternion globalRotBToUCorrection = Quaternion.AngleAxis(-90, new Vector3(1, 0, 0));
        public static Quaternion globalRotUToBCorrection = Quaternion.AngleAxis(90, new Vector3(1, 0, 0));
        public static float DegToRadFactor = Mathf.PI / 180.0f;
        public static float RadToDegFactor = 180.0f / Mathf.PI;
        public static float DegToRad(float deg)
        {
            return deg * DegToRadFactor;
        }

        public static float RadToDeg(float rad)
        {
            return rad * RadToDegFactor;
        }

        // Network rotations are authored in the training frame (SMPL/Blender:
        // Z-up, right-handed); Unity is Y-up, left-handed. Two separate fixes are
        // needed: (1) per-joint HANDEDNESS (right- -> left-handed), done here by
        // negating axis.x of each rotation; (2) the world UP-axis swap (Z-up ->
        // Y-up), which only the root references and is applied as pelvisCorrection
        // in AnimUtils.SetPose, not here.
        public static void SixDToQuaternion(float[] sixd_blender, ref Quaternion[] local_rotations_unity)
        {
            for (int i = 0; i < 22; i++)
            {
                Vector3 vec1 = new Vector3(sixd_blender[i * 6], sixd_blender[i * 6 + 1], sixd_blender[i * 6 + 2]);
                Vector3 vec2 = new Vector3(sixd_blender[i * 6 + 3], sixd_blender[i * 6 + 4], sixd_blender[i * 6 + 5]);
                Vector3 vec3 = Vector3.Cross(vec1, vec2);

                Matrix4x4 rotationMatrix = new Matrix4x4();
                rotationMatrix.SetRow(0, new Vector4(vec1.x, vec1.y, vec1.z, 0));
                rotationMatrix.SetRow(1, new Vector4(vec2.x, vec2.y, vec2.z, 0));
                rotationMatrix.SetRow(2, new Vector4(vec3.x, vec3.y, vec3.z, 0));
                rotationMatrix.SetRow(3, new Vector4(0, 0, 0, 1));
                // Convert to Unity coordinate system
                Quaternion quaternion = rotationMatrix.rotation;
                quaternion.ToAngleAxis(out float angle, out Vector3 axis);
                axis[0] = -axis[0]; // handedness flip: right-handed (Blender/SMPL) -> left-handed (Unity)
                local_rotations_unity[i] = Quaternion.AngleAxis(angle, axis);
            }
        }

        public static void SixDToQuaternion2(float[] sixd_blender, ref Quaternion[] local_rotations_unity)
        {
            for (int i = 0; i < 22; i++)
            {
                Vector3 vec1 = new Vector3(sixd_blender[i * 6], sixd_blender[i * 6 + 1], sixd_blender[i * 6 + 2]);
                Vector3 vec2 = new Vector3(sixd_blender[i * 6 + 3], sixd_blender[i * 6 + 4], sixd_blender[i * 6 + 5]);
                Vector3 vec3 = Vector3.Cross(vec1, vec2);

                Matrix4x4 rotationMatrix = new Matrix4x4();
                rotationMatrix.SetRow(0, new Vector4(vec1.x, vec2.x, vec3.x, 0));
                rotationMatrix.SetRow(1, new Vector4(vec1.y, vec2.y, vec3.y, 0));
                rotationMatrix.SetRow(2, new Vector4(vec1.z, vec2.z, vec3.z, 0));
                rotationMatrix.SetRow(3, new Vector4(0, 0, 0, 1));
                // Convert to Unity coordinate system
                Quaternion quaternion = rotationMatrix.rotation;
                quaternion.ToAngleAxis(out float angle, out Vector3 axis);
                Vector3 aa = axis * angle * DegToRadFactor;
                local_rotations_unity[i] = GlobalRotationBlenderToUnity(aa);
            }
        }

        public static void QuaternionToSixD(Quaternion[] local_rotations_unity, ref float[] sixd_blender)
        {
            for (int i = 0; i < 22; i++)
            {
                Quaternion quat = local_rotations_unity[i];
                quat.ToAngleAxis(out float angle, out Vector3 axis);
                axis[0] = -axis[0];
                quat = Quaternion.AngleAxis(angle, axis);
                Matrix4x4 rotationMatrix = Matrix4x4.Rotate(quat);
                Vector4 vec1 = rotationMatrix.GetRow(0);
                Vector4 vec2 = rotationMatrix.GetRow(1);
                sixd_blender[i * 6] = vec1.x;
                sixd_blender[i * 6 + 1] = vec1.y;
                sixd_blender[i * 6 + 2] = vec1.z;
                sixd_blender[i * 6 + 3] = vec2.x;
                sixd_blender[i * 6 + 4] = vec2.y;
                sixd_blender[i * 6 + 5] = vec2.z;
            }
        }

        public static Vector3 BlenderToUnity(Vector3 pos)
        {
            return new Vector3(-pos.x, pos.z, -pos.y);
        }

        public static Vector3 UnityToBlender(Vector3 pos)
        {
            return new Vector3(-pos.x, -pos.z, pos.y);
        }

        public static Quaternion GlobalRotationBlenderToUnity(Vector3 aa_blender)
        {
            float angle = -aa_blender.magnitude;
            Vector3 axis = new Vector3(-aa_blender.x, aa_blender.z, -aa_blender.y);
            return Quaternion.AngleAxis(RadToDeg(angle), axis) * globalRotBToUCorrection;
        }

        public static Vector3 GlobalRotationUnityToBlender(Quaternion rot_unity)
        {
            Quaternion rot = rot_unity * globalRotUToBCorrection;
            rot.ToAngleAxis(out float angle, out Vector3 axis);
            return new Vector3(-axis.x, -axis.z, axis.y) * DegToRad(-angle);
        }

        public static void PrintAA(Quaternion rot)
        {
            rot.ToAngleAxis(out float angle, out Vector3 axis);
            Debug.Log(string.Format("angle axis: {0}", DegToRad(angle) * axis));
        }

        public static void PrintRotationMatrix(Quaternion rot)
        {
            Matrix4x4 matRot = Matrix4x4.Rotate(rot);
            for (int i = 0; i < 3; i++)
            {
                Debug.Log(string.Format("{0}, {1}, {2}", matRot[i, 0], matRot[i, 1], matRot[i, 2]));
            }
        }
    }

    // CSV IO was removed in favour of NumPy (.npy/.npz) fixtures — see
    // VrChat.NpyReader (Runtime/NpyReader.cs), which is more
    // robust (typed, shape-checked, binary) than the old comma-split text format.

    public static class AnimUtils
    {
        public const int boneNum = 22;
        public static string[] boneNames = new string[] {
            "pelvis", "left_hip", "right_hip", "spine1", "left_knee", "right_knee", "spine2", "left_ankle", "right_ankle",
            "spine3", "left_foot", "right_foot", "neck", "left_collar", "right_collar", "head", "left_shoulder", "right_shoulder",
            "left_elbow", "right_elbow", "left_wrist", "right_wrist"
        };
        // Root-only world UP-axis fix: rotates the training frame's Z-up to Unity's
        // Y-up (Rx(-90) maps +Z -> +Y). Applied to the pelvis in SetPose because
        // joint 0 is the only world-referenced rotation; joints 1-21 are
        // parent-relative and inherit the fix through the kinematic chain. Without
        // it the body's up-axis points along Unity +Z and the avatar lies flat.
        // (Handedness is handled separately, per joint, in MathUtils.SixDToQuaternion.)
        public static Quaternion pelvisCorrection = Quaternion.AngleAxis(-90, new Vector3(1, 0, 0));
        public static Quaternion invPelvisCorrection = Quaternion.AngleAxis(90, new Vector3(1, 0, 0));
        public static int[] upper_idx = new int[] { 4, 6, 9, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 };

        public static void BindBones(Transform transform, ref Transform[] bones)
        {
            bool allBonesFound = true;
            for (int i = 0; i < boneNum; i++)
            {
                bones[i] = FindChildRecursive(transform, boneNames[i]);
                if (bones[i] == null)
                {
                    Debug.LogError(boneNames[i] + " bone not found.");
                    allBonesFound = false;
                }
            }
            if (allBonesFound) Debug.Log("All bones found.");
        }

        public static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    return child;

                // Recursive search in child"s children
                Transform result = FindChildRecursive(child, childName);
                if (result != null)
                    return result;
            }
            return null;
        }

        public static void SetBoneRotationRodrigues(ref Transform[] bones, int boneIndex, Vector3 rodrigues)
        {
            bones[boneIndex].localRotation = Quaternion.AngleAxis(rodrigues.magnitude * 180 / Mathf.PI, rodrigues.normalized);
        }

        public static void SetBoneRotation(ref Transform[] bones, int boneIndex, Quaternion rotation)
        {
            bones[boneIndex].localRotation = rotation;
        }

        public static void SetPose(ref Transform[] bones, Vector3[] rotations_aa, int start = 0)
        {
            SetBoneRotation(ref bones, 0, pelvisCorrection * Quaternion.AngleAxis(rotations_aa[start].magnitude * 180 / Mathf.PI, rotations_aa[start].normalized));
            for (int i = 1; i < boneNum; i++)
            {
                SetBoneRotationRodrigues(ref bones, i, rotations_aa[start + i]);
            }
        }

        public static void SetPose(ref Transform[] bones, ref Quaternion[] rotations, int start = 0)
        {
            SetBoneRotation(ref bones, 0, pelvisCorrection * rotations[start]);
            for (int i = 1; i < boneNum; i++)
            {
                SetBoneRotation(ref bones, i, rotations[start + i]);
            }
        }

        public static void SetUpperPose(ref Transform[] bones, ref Quaternion[] rotations, int start = 0)
        {
            SetBoneRotation(ref bones, 0, pelvisCorrection * rotations[start]);
            foreach (int i in upper_idx)
            {
                SetBoneRotation(ref bones, i, rotations[start + i]);
            }
        }

        public static void SetPosition(ref Transform[] bones, Transform characterRoot, Vector3 headTargetPosition)
        {
            Vector3 headOffset = bones[15].position - characterRoot.position;
            characterRoot.position = headTargetPosition - headOffset;
        }

        /// <summary>
        /// Two-bone IK by cyclic coordinate descent: rotates <paramref name="mid"/> then
        /// <paramref name="upper"/> (a few passes) so <paramref name="end"/> reaches
        /// <paramref name="target"/> — or comes as close as the bone lengths allow. Simple and
        /// robust (no analytic edge cases); the elbow bend is whatever falls out of the start
        /// pose, which is fine for seeding the warm-up so the wrists begin at the tracked
        /// controllers (the model refines from there). World rotations are written.
        /// </summary>
        public static void SolveTwoBoneIK(Transform upper, Transform mid, Transform end, Vector3 target, int iterations = 8)
        {
            for (int it = 0; it < iterations; it++)
            {
                CcdStep(mid, end, target);
                CcdStep(upper, end, target);
            }
        }

        static void CcdStep(Transform joint, Transform end, Vector3 target)
        {
            Vector3 toEnd = end.position - joint.position;
            Vector3 toTarget = target - joint.position;
            if (toEnd.sqrMagnitude < 1e-10f || toTarget.sqrMagnitude < 1e-10f) return;
            joint.rotation = Quaternion.FromToRotation(toEnd, toTarget) * joint.rotation;
        }
    }
}