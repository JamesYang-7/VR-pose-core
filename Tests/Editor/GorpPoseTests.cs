using MyUtils;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrChat.Testing
{
    [TestFixture]
    public class GorpPoseTests
    {
        static readonly int[] kParents = { -1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 12, 13, 14, 16, 17, 18, 19 };
        GameObject _rig;

        [TearDown]
        public void TearDown()
        {
            if (_rig != null) Object.DestroyImmediate(_rig);
        }

        // Right-handed rotation (Rodrigues) as row-major 3x3, the joint_rotmat layout.
        static float[] RotationMatrix(Vector3 axis, float degrees)
        {
            var k = axis.normalized;
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a), t = 1 - c;
            return new[]
            {
                t * k.x * k.x + c,       t * k.x * k.y - s * k.z, t * k.x * k.z + s * k.y,
                t * k.x * k.y + s * k.z, t * k.y * k.y + c,       t * k.y * k.z - s * k.x,
                t * k.x * k.z - s * k.y, t * k.y * k.z + s * k.x, t * k.z * k.z + c,
            };
        }

        static float[] Repeat(float[] block, int times)
        {
            var a = new float[block.Length * times];
            for (int i = 0; i < times; i++) block.CopyTo(a, i * block.Length);
            return a;
        }

        [TestCase(1f, 0f, 0f, 90f)]
        [TestCase(0f, 0f, 1f, -35f)]
        [TestCase(0.3f, -0.8f, 0.5f, 120f)]
        public void RotationsFromMatrices_AnyRotation_AgreesWithTheProjectSixDDecode(float x, float y, float z, float degrees)
        {
            var m = RotationMatrix(new Vector3(x, y, z), degrees);
            var sixD = new[] { m[0], m[3], m[6], m[1], m[4], m[7] }; // first two columns
            var expected = new Quaternion[PoseLayout.Joints];
            MyUtils.MathUtils.SixDToQuaternion(Repeat(sixD, PoseLayout.Joints), ref expected);
            var actual = new Quaternion[PoseLayout.Joints];

            GorpPose.RotationsFromMatrices(Repeat(m, PoseLayout.Joints), actual);

            Assert.That(Quaternion.Angle(actual[5], expected[5]), Is.LessThan(1e-2f));
        }

        // GORP's conversion makes the headset the head joint's frame, and the SMPL-X rig's rest head is level and
        // looks along +z: so the head joint's world rotation is the headset's.
        [TestCase(0f, 0f, 0f)]
        [TestCase(0f, 90f, 0f)]
        [TestCase(20f, 45f, -10f)]
        public void WorldRotation_HeadJointOfAHeadsetUnderAnUnturnedRoot_IsTheHeadsetRotation(float x, float y, float z)
        {
            var headset = Quaternion.Euler(x, y, z);
            var raw = new float[GorpDevice.RawWidth];
            GorpDevice.FromUnity(new Vector3(0f, 1.7f, 0f), headset, 1f, raw, 0);
            var position = new double[3];
            var rotation = new double[9];
            GorpDevice.ToSmpl(raw, 0, GorpDevice.Head, position, rotation);

            var actual = GorpPose.WorldRotation(rotation, Quaternion.identity);

            Assert.That(Quaternion.Angle(actual, headset), Is.LessThan(0.01f));
        }

        [Test]
        public void Apply_GroundTruthFrameOfS26_PointsEveryBoneAlongTheRowsJointPositions()
        {
            ModelLocalFiles.RequireRpm();
            var seq = ModelExport.Rpm.LoadSequence("s26_fruitninja-q3-controllers_000");
            _rig = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SMPLX/smplx-visemes-unity.fbx"));
            var bones = new Transform[PoseLayout.Joints];
            AnimUtils.BindBones(_rig.transform, ref bones);
            const int frame = 450;
            var rotations = new Quaternion[PoseLayout.Joints];
            GorpPose.RotationsFromSixD(seq.GroundTruth, frame * PoseLayout.MotionWidth, rotations);
            var pelvis = GorpPose.ToUnity(GorpSequence.JointPosition(seq.GroundTruth, frame, 0));

            GorpPose.Apply(bones, rotations, pelvis);

            // Residual is the subject's skeleton vs the rig's (2.2° mean, 6° worst, the same on every frame).
            float worst = MaxBoneAngle(seq.GroundTruth, frame, bones);
            Assert.That(worst, Is.LessThan(8f), "worst bone direction (deg)");
            Assert.That((bones[0].position - pelvis).magnitude, Is.LessThan(1e-4f), "pelvis on target (m)");
        }

        [Test]
        public void RestJoints_SmplxRig_MatchesTheBundleSkeletonUpToBodyShape()
        {
            ModelLocalFiles.RequireRpm();
            var seq = ModelExport.Rpm.LoadSequence("s26_fruitninja-q3-controllers_000");
            _rig = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SMPLX/smplx-visemes-unity.fbx"));
            var bones = new Transform[PoseLayout.Joints];
            AnimUtils.BindBones(_rig.transform, ref bones);

            var actual = GorpPose.RestJoints(bones);

            // The rig and the GORP subject differ in shape only: 6° and 3.6 cm at worst. A mirrored or turned
            // skeleton would be off by tens of degrees.
            float worstAngle = 0f, worstLength = 0f;
            for (int j = 1; j < PoseLayout.Joints; j++)
            {
                var bone = Bone(actual, j);
                var reference = Bone(seq.JointsRest, j);
                worstAngle = Mathf.Max(worstAngle, Vector3.Angle(bone, reference));
                worstLength = Mathf.Max(worstLength, Mathf.Abs(bone.magnitude - reference.magnitude));
            }
            Assert.That(worstAngle, Is.LessThan(10f), "worst bone direction (deg)");
            Assert.That(worstLength, Is.LessThan(0.06f), "worst bone length (m)");
        }

        [Test]
        public void RestJoints_PosedRig_LeavesThePoseAsItWas()
        {
            _rig = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SMPLX/smplx-visemes-unity.fbx"));
            var bones = new Transform[PoseLayout.Joints];
            AnimUtils.BindBones(_rig.transform, ref bones);
            for (int j = 0; j < bones.Length; j++) bones[j].localRotation = Quaternion.Euler(j * 3f, j * 5f, -j * 2f);
            var expected = new Quaternion[bones.Length];
            for (int j = 0; j < bones.Length; j++) expected[j] = bones[j].localRotation;

            GorpPose.RestJoints(bones);

            for (int j = 0; j < bones.Length; j++)
                Assert.That(Quaternion.Angle(bones[j].localRotation, expected[j]), Is.LessThan(1e-3f), $"bone {j}");
        }

        static Vector3 Bone(float[] joints, int j)
        {
            int p = kParents[j];
            return new Vector3(joints[j * 3] - joints[p * 3], joints[j * 3 + 1] - joints[p * 3 + 1], joints[j * 3 + 2] - joints[p * 3 + 2]);
        }

        static float MaxBoneAngle(float[] motion, int frame, Transform[] bones)
        {
            float worst = 0f;
            for (int j = 1; j < PoseLayout.Joints; j++)
            {
                var want = GorpPose.ToUnity(GorpSequence.JointPosition(motion, frame, j) - GorpSequence.JointPosition(motion, frame, kParents[j]));
                worst = Mathf.Max(worst, Vector3.Angle(want, bones[j].position - bones[kParents[j]].position));
            }
            return worst;
        }
    }
}
