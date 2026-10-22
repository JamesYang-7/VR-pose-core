using MyUtils;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrChat.Testing
{
    [TestFixture]
    public class RigCalibrationTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        Transform[] SmplxBones()
        {
            _root = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SMPLX/smplx-visemes-unity.fbx"));
            var bones = new Transform[AnimUtils.boneNum];
            AnimUtils.BindBones(_root.transform, ref bones);
            return bones;
        }

        [Test]
        public void HeadsetAboveHeadJoint_UprightHeadset_IsGorpsVerticalHeadOffset()
        {
            var actual = RigCalibration.HeadsetAboveHeadJoint();

            // RPM-README §4.2: the head joint is 0.02414 m below the headset along its up axis.
            Assert.That(actual, Is.EqualTo(0.02414f).Within(1e-4f));
        }

        [Test]
        public void TPoseHeadsetHeight_SmplxCharacter_IsItsHeadJointAboveItsSolesPlusTheHeadsetOffset()
        {
            var bones = SmplxBones();

            var actual = RigCalibration.TPoseHeadsetHeight(bones, _root.GetComponentsInChildren<SkinnedMeshRenderer>());

            // The SMPL-X head joint stands 1.638 m above its soles in the T-pose; the headset sits 0.024 m higher.
            Assert.That(actual, Is.EqualTo(1.662f).Within(0.005f));
        }

        [Test]
        public void TPoseHeadsetHeight_CharacterPosedAndLifted_MeasuresTheRestPoseAndRestoresThePose()
        {
            var bones = SmplxBones();
            _root.transform.position = new Vector3(0.5f, 0.3f, -1f);
            bones[4].localRotation = Quaternion.Euler(80f, 0f, 0f); // left knee bent
            bones[16].localRotation = Quaternion.Euler(0f, 0f, 60f); // left shoulder
            var knee = bones[4].localRotation;

            var actual = RigCalibration.TPoseHeadsetHeight(bones, _root.GetComponentsInChildren<SkinnedMeshRenderer>());

            Assert.That(actual, Is.EqualTo(1.662f).Within(0.005f), "height");
            Assert.That(Quaternion.Angle(bones[4].localRotation, knee), Is.LessThan(1e-3f), "knee restored");
        }

        // A wearer of the SMPL-X character's size in its T-pose: wrists 1.42 m apart, 0.29 m below the eyes.
        static readonly Vector3 kHead = new(0f, 1.66f, 0.08f);
        static readonly Vector3 kLeft = new(-0.75f, 1.38f, 0f);
        static readonly Vector3 kRight = new(0.75f, 1.38f, 0f);

        [TestCase(0f)]
        [TestCase(0.45f)] // the runtime's floor 0.45 m off: still a T-pose
        [TestCase(-0.3f)]
        public void CheckTPose_ArmsSpreadLevel_NoMisses(float floorError)
        {
            var lift = Vector3.up * floorError;

            var actual = RigCalibration.CheckTPose(kHead + lift, Vector3.up, kLeft + lift, kRight + lift);

            Assert.That(actual, Is.EqualTo(TPoseMisses.None));
        }

        [Test]
        public void CheckTPose_ArmsDown_HandsNotAtShouldersAndTooClose()
        {
            var actual = RigCalibration.CheckTPose(kHead, Vector3.up, new Vector3(-0.25f, 0.85f, 0f), new Vector3(0.25f, 0.85f, 0f));

            Assert.That(actual, Is.EqualTo(TPoseMisses.HandsNotAtShoulders | TPoseMisses.HandsTooClose));
        }

        [Test]
        public void CheckTPose_HandsTogetherInFront_HandsTooClose()
        {
            var actual = RigCalibration.CheckTPose(kHead, Vector3.up, new Vector3(-0.1f, 1.3f, 0.45f), new Vector3(0.1f, 1.3f, 0.45f));

            Assert.That(actual, Is.EqualTo(TPoseMisses.HandsTooClose));
        }

        [Test]
        public void CheckTPose_OneArmRaised_ArmsNotLevel()
        {
            var actual = RigCalibration.CheckTPose(kHead, Vector3.up, new Vector3(-0.75f, 1.62f, 0f), kRight);

            Assert.That(actual, Is.EqualTo(TPoseMisses.ArmsNotLevel));
        }

        [Test]
        public void CheckTPose_HeadTiltedDown_HeadTilted()
        {
            var headUp = Quaternion.Euler(45f, 0f, 0f) * Vector3.up;

            var actual = RigCalibration.CheckTPose(kHead, headUp, kLeft, kRight);

            Assert.That(actual, Is.EqualTo(TPoseMisses.HeadTilted));
        }

        [TestCase(0.6f, 0.08f)] // beside one hand
        [TestCase(0f, -0.5f)] // well behind the hands
        public void CheckTPose_HeadAwayFromBetweenTheHands_HeadNotBetweenHands(float x, float z)
        {
            var actual = RigCalibration.CheckTPose(new Vector3(x, 1.66f, z), Vector3.up, kLeft, kRight);

            Assert.That(actual, Is.EqualTo(TPoseMisses.HeadNotBetweenHands));
        }

        [TestCase(0.85f, 0f)] // arms hanging
        [TestCase(1.1f, 0.35f)] // controllers held in front at the waist
        public void IsAtRest_HandsLowered_True(float handHeight, float handForward)
        {
            var actual = RigCalibration.IsAtRest(kHead, Vector3.up,
                new Vector3(-0.22f, handHeight, handForward), new Vector3(0.22f, handHeight, handForward));

            Assert.That(actual, Is.True);
        }

        [TestCase(0.45f)] // the runtime's floor 0.45 m off
        [TestCase(-0.3f)]
        public void IsAtRest_RuntimeFloorOff_StillTrue(float floorError)
        {
            var lift = Vector3.up * floorError;

            var actual = RigCalibration.IsAtRest(kHead + lift, Vector3.up,
                new Vector3(-0.22f, 0.85f, 0f) + lift, new Vector3(0.22f, 0.85f, 0f) + lift);

            Assert.That(actual, Is.True);
        }

        [Test]
        public void IsAtRest_HeadsetOnADeskBesideTheControllers_False()
        {
            var actual = RigCalibration.IsAtRest(new Vector3(0f, 0.75f, 0f), Vector3.up,
                new Vector3(-0.3f, 0.73f, 0f), new Vector3(0.3f, 0.73f, 0f));

            Assert.That(actual, Is.False);
        }

        [Test]
        public void IsAtRest_HandsUpPuttingTheHeadsetOn_False()
        {
            var actual = RigCalibration.IsAtRest(kHead, Vector3.up,
                new Vector3(-0.12f, 1.62f, 0.05f), new Vector3(0.12f, 1.62f, 0.05f));

            Assert.That(actual, Is.False);
        }

        [Test]
        public void IsAtRest_HeadTiltedDown_False()
        {
            var headUp = Quaternion.Euler(45f, 0f, 0f) * Vector3.up;

            var actual = RigCalibration.IsAtRest(kHead, headUp, new Vector3(-0.22f, 0.85f, 0f), new Vector3(0.22f, 0.85f, 0f));

            Assert.That(actual, Is.False);
        }

        [Test]
        public void IsAtRest_ControllerLeftOnATableAway_False()
        {
            var actual = RigCalibration.IsAtRest(kHead, Vector3.up, new Vector3(-0.22f, 0.85f, 0f), new Vector3(1.5f, 0.75f, 0.5f));

            Assert.That(actual, Is.False);
        }

        // An XR Origin turned and placed off the scene's origin, with a head under it looking along its own yaw.
        (Transform origin, Transform head) Rig(float originYaw, float headYaw)
        {
            _root = new GameObject("XR Origin");
            _root.transform.SetPositionAndRotation(new Vector3(0.4f, 0f, -0.3f), Quaternion.Euler(0f, originYaw, 0f));
            var head = new GameObject("Head").transform;
            head.SetParent(_root.transform, worldPositionStays: false);
            head.SetLocalPositionAndRotation(new Vector3(0.3f, 1.7f, 0.2f), Quaternion.Euler(10f, headYaw, 0f));
            return (_root.transform, head);
        }

        [TestCase(0f, 0f)]
        [TestCase(30f, 120f)]
        [TestCase(-75f, -150f)]
        public void TurnToFace_AnyHeading_HeadFacesTheTargetWithoutMoving(float originYaw, float headYaw)
        {
            var (origin, head) = Rig(originYaw, headYaw);
            var headBefore = head.position;
            var target = new Vector3(0f, 1.38f, 2f);

            RigCalibration.TurnToFace(origin, head, target);

            var heading = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var toTarget = Vector3.ProjectOnPlane(target - head.position, Vector3.up);
            Assert.That(Vector3.Angle(heading, toTarget), Is.LessThan(0.01f), "heading to target (deg)");
            Assert.That((head.position - headBefore).magnitude, Is.LessThan(1e-4f), "head moved (m)");
        }

        [Test]
        public void TurnToFace_TargetToTheRight_ReturnsAClockwiseTurn()
        {
            var (origin, head) = Rig(0f, 0f);

            var actual = RigCalibration.TurnToFace(origin, head, head.position + new Vector3(2f, 0f, 2f));

            Assert.That(actual, Is.EqualTo(45f).Within(0.01f));
        }
    }
}
