using MyUtils;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrChat.Ik;

namespace VrChat.Testing
{
    [TestFixture]
    public class IkBodyTests
    {
        const int kSpine3 = 9, kLeftShoulder = 16, kLeftElbow = 18, kLeftWrist = 20, kRightWrist = 21, kLeftFoot = 10;

        // A wearer of the SMPL-X character's size standing upright where it stands (its eyes at rest), holding the
        // controllers in front at the waist, the wrists level with the palms facing in.
        static readonly Vector3 kEyes = new(0f, 1.685f, 0.063f);
        static readonly Pose kLeftWristPose = new(new Vector3(-0.2f, 1.05f, 0.3f), Quaternion.Euler(0f, 0f, 90f));
        static readonly Pose kRightWristPose = new(new Vector3(0.2f, 1.05f, 0.3f), Quaternion.Euler(0f, 0f, -90f));

        GameObject _root;
        Transform[] _bones;
        EyeAnchor _eyes;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        IkBody SmplxBody()
        {
            _root = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SMPLX/smplx-visemes-unity.fbx"));
            _bones = new Transform[AnimUtils.boneNum];
            AnimUtils.BindBones(_root.transform, ref _bones);
            _eyes = new EyeAnchor(_bones[15], AnimUtils.FindChildRecursive(_root.transform, "left_eye_smplhf"),
                AnimUtils.FindChildRecursive(_root.transform, "right_eye_smplhf"));
            return new IkBody(_bones, _eyes, _root.transform.forward);
        }

        static Quaternion Yaw(float degrees) => Quaternion.AngleAxis(degrees, Vector3.up);

        [Test]
        public void Solve_UprightWithTheWristsInReach_PutsTheEyesAndWristsOnTheirTargets()
        {
            var sut = SmplxBody();
            var eyes = kEyes + new Vector3(0.5f, 0f, -1f);
            var offset = eyes - kEyes;
            var left = new Pose(kLeftWristPose.position + offset, kLeftWristPose.rotation);
            var right = new Pose(kRightWristPose.position + offset, kRightWristPose.rotation);

            sut.Solve(eyes, Quaternion.identity, left, right);

            Assert.That((_eyes.EyesPosition - eyes).magnitude, Is.LessThan(0.001f), "eyes");
            Assert.That((_bones[kLeftWrist].position - left.position).magnitude, Is.LessThan(0.001f), "left wrist");
            Assert.That((_bones[kRightWrist].position - right.position).magnitude, Is.LessThan(0.001f), "right wrist");
            Assert.That(Quaternion.Angle(_bones[kLeftWrist].rotation, left.rotation), Is.LessThan(0.01f), "left wrist rotation");
            Assert.That(Quaternion.Angle(_bones[kRightWrist].rotation, right.rotation), Is.LessThan(0.01f), "right wrist rotation");
        }

        [Test]
        public void Solve_UprightAtTheRestEyes_KeepsTheFeetWhereTheyStandAtRest()
        {
            var sut = SmplxBody();
            var foot = _bones[kLeftFoot].position;

            sut.Solve(kEyes, Quaternion.identity, kLeftWristPose, kRightWristPose);

            Assert.That((_bones[kLeftFoot].position - foot).magnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void Solve_HandsInFrontAtTheWaist_HangsTheUpperArmsNearlyStraightDown()
        {
            var sut = SmplxBody();

            sut.Solve(kEyes, Quaternion.identity, kLeftWristPose, kRightWristPose);

            var upperArm = _bones[kLeftElbow].position - _bones[kLeftShoulder].position;
            Assert.That(Vector3.Angle(upperArm, Vector3.down), Is.LessThan(30f));
        }

        [Test]
        public void Solve_HeadTurnedWithinTheDeadZone_KeepsTheHeadingAndTwistsTheChest()
        {
            var sut = SmplxBody();
            sut.Solve(kEyes, Quaternion.identity, kLeftWristPose, kRightWristPose);

            sut.Solve(kEyes, Yaw(30f), kLeftWristPose, kRightWristPose);

            // spine1, spine2 and spine3 add 10, 15 and 20% of the turn.
            var chest = Vector3.SignedAngle(Vector3.forward,
                Vector3.ProjectOnPlane(_bones[kSpine3].rotation * Vector3.forward, Vector3.up), Vector3.up);
            Assert.That(sut.Heading, Is.EqualTo(0f).Within(1e-3f), "heading");
            Assert.That(chest, Is.EqualTo(13.5f).Within(1.5f), "chest");
            Assert.That((_eyes.EyesPosition - kEyes).magnitude, Is.LessThan(0.001f), "eyes");
        }

        [Test]
        public void Solve_HeadTurnedPastTheDeadZone_TurnsTheBodyByTheRest()
        {
            var sut = SmplxBody();
            sut.Solve(kEyes, Quaternion.identity, kLeftWristPose, kRightWristPose);

            sut.Solve(kEyes, Yaw(70f), kLeftWristPose, kRightWristPose);

            Assert.That(sut.Heading, Is.EqualTo(70f - sut.TurnDeadZone).Within(1e-3f));
        }

        [Test]
        public void Solve_EyesMovedWithinTheFollowRadius_LeansWithTheFeetStill()
        {
            var sut = SmplxBody();
            sut.Solve(kEyes, Quaternion.identity, kLeftWristPose, kRightWristPose);
            var stance = sut.Stance;
            var foot = _bones[kLeftFoot].position;
            var eyes = kEyes + new Vector3(0f, -0.02f, 0.12f);

            sut.Solve(eyes, Quaternion.identity, kLeftWristPose, kRightWristPose);

            Assert.That((sut.Stance - stance).magnitude, Is.LessThan(1e-5f), "stance");
            Assert.That((_bones[kLeftFoot].position - foot).magnitude, Is.LessThan(0.002f), "foot");
            Assert.That((_eyes.EyesPosition - eyes).magnitude, Is.LessThan(0.001f), "eyes");
        }

        [Test]
        public void Solve_EyesMovedPastTheFollowRadius_DragsTheStanceAlongToTheRadius()
        {
            var sut = SmplxBody();
            sut.Solve(kEyes, Quaternion.identity, kLeftWristPose, kRightWristPose);
            var stance = sut.Stance;
            var step = new Vector3(0.4f, 0f, 0f);

            sut.Solve(kEyes + step, Quaternion.identity, kLeftWristPose, kRightWristPose);

            Assert.That((sut.Stance - stance - step * (1f - sut.FollowRadius / step.magnitude)).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void Solve_EyesTooLowForTheSpine_StillPutsTheEyesOnTheTarget()
        {
            var sut = SmplxBody();
            sut.Solve(kEyes, Quaternion.identity, kLeftWristPose, kRightWristPose);
            var eyes = kEyes + new Vector3(0f, -0.4f, 0f);

            sut.Solve(eyes, Quaternion.identity, kLeftWristPose, kRightWristPose);

            Assert.That((_eyes.EyesPosition - eyes).magnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void Solve_WristOutOfReach_StretchesTheArmTowardsIt()
        {
            var sut = SmplxBody();
            var far = new Pose(new Vector3(-1.5f, 1.5f, 0.5f), Quaternion.identity);

            sut.Solve(kEyes, Quaternion.identity, far, kRightWristPose);

            var shoulder = _bones[kLeftShoulder].position;
            Assert.That(Vector3.Angle(_bones[kLeftWrist].position - shoulder, far.position - shoulder), Is.LessThan(1f));
        }

        [Test]
        public void Solve_SameTargetsAgain_GivesTheSamePose()
        {
            var sut = SmplxBody();
            var eyes = kEyes + new Vector3(0.05f, -0.03f, 0.08f);
            var head = Quaternion.Euler(20f, 25f, 0f);
            sut.Solve(eyes, head, kLeftWristPose, kRightWristPose);
            var before = new Quaternion[_bones.Length];
            for (var j = 0; j < _bones.Length; j++) before[j] = _bones[j].rotation;

            sut.Solve(eyes, head, kLeftWristPose, kRightWristPose);

            for (var j = 0; j < _bones.Length; j++)
                Assert.That(Quaternion.Angle(_bones[j].rotation, before[j]), Is.LessThan(0.01f), AnimUtils.boneNames[j]);
        }
    }
}
