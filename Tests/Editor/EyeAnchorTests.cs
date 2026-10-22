using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace VrChat.Testing
{
    /// <summary>
    /// A head bone with its eyes 5 cm up and 7 cm forward, hung (as on a CC5 rig) under an
    /// intermediate "facial" bone with its own rotation, so the eyes are not direct children.
    /// </summary>
    [TestFixture]
    public class EyeAnchorTests
    {
        static readonly Vector3 kEyesInHead = new(0f, 0.05f, 0.07f);
        static readonly Vector3EqualityComparer kMm = new(1e-3f);

        GameObject _root;
        Transform _head, _leftEye, _rightEye;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("EyeAnchorTests");
            _head = NewBone("head", _root.transform, new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            var facial = NewBone("facial", _head, new Vector3(0f, 0.02f, 0f), Quaternion.Euler(-90f, 0f, 30f));
            _leftEye = NewBone("l_eye", facial, Vector3.zero, Quaternion.identity);
            _rightEye = NewBone("r_eye", facial, Vector3.zero, Quaternion.identity);
            _leftEye.position = _head.TransformPoint(kEyesInHead + new Vector3(-0.03f, 0f, 0f));
            _rightEye.position = _head.TransformPoint(kEyesInHead + new Vector3(0.03f, 0f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        static Transform NewBone(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            return t;
        }

        Vector3 EyeMidpoint() => (_leftEye.position + _rightEye.position) * 0.5f;

        [Test]
        public void EyesPosition_HeadMovedAndTurned_ReturnsTheEyeMidpoint()
        {
            var sut = new EyeAnchor(_head, _leftEye, _rightEye);
            _head.SetPositionAndRotation(new Vector3(0.4f, 1.2f, -0.3f), Quaternion.Euler(25f, -70f, 10f));

            var actual = sut.EyesPosition;

            Assert.That(actual, Is.EqualTo(EyeMidpoint()).Using(kMm));
        }

        [Test]
        public void EyesPosition_HeadCollapsedAfterMeasuring_ReturnsTheUncollapsedEyes()
        {
            var sut = new EyeAnchor(_head, _leftEye, _rightEye);
            var expected = EyeMidpoint();
            _head.localScale = Vector3.one * 1e-4f;

            var actual = sut.EyesPosition;

            Assert.That(actual, Is.EqualTo(expected).Using(kMm));
        }

        [Test]
        public void EyesPosition_MeasuredWhileHeadCollapsed_ReturnsTheUncollapsedEyes()
        {
            var expected = EyeMidpoint();
            _head.localScale = Vector3.one * 1e-4f;
            var sut = new EyeAnchor(_head, _leftEye, _rightEye);

            var actual = sut.EyesPosition;

            Assert.That(actual, Is.EqualTo(expected).Using(kMm));
        }

        [Test]
        public void EyesPosition_AvatarScaledAfterMeasuring_ScalesTheOffset()
        {
            var sut = new EyeAnchor(_head, _leftEye, _rightEye);
            _root.transform.localScale = Vector3.one * 2f;

            var actual = sut.EyesPosition - _head.position;

            Assert.That(actual, Is.EqualTo(_head.rotation * (kEyesInHead * 2f)).Using(kMm));
        }

        [Test]
        public void EyesInFrame_FrameMatchesTheHeadsFacing_ReturnsUpAndForwardOffset()
        {
            _head.rotation = Quaternion.Euler(0f, 90f, 0f);
            var sut = new EyeAnchor(_head, _leftEye, _rightEye);

            var actual = sut.EyesInFrame(Quaternion.Euler(0f, 90f, 0f));

            Assert.That(actual, Is.EqualTo(kEyesInHead).Using(kMm));
        }

        [Test]
        public void EyesInFrame_HeadsetPoseOnTheEyes_RecoversTheHeadPosition()
        {
            var facing = Quaternion.Euler(0f, 180f, 0f);
            _head.rotation = facing * Quaternion.Euler(-90f, 0f, 0f); // a rig whose head axes are not the facing's
            var sut = new EyeAnchor(_head, _leftEye, _rightEye);
            var eyesInHeadset = sut.EyesInFrame(facing);
            var turn = Quaternion.Euler(-20f, 35f, 5f);
            _head.SetPositionAndRotation(new Vector3(1f, 1.5f, 2f), turn * _head.rotation);
            var headset = new Pose(sut.EyesPosition, turn * facing);

            var actual = headset.position - headset.rotation * eyesInHeadset;

            Assert.That(actual, Is.EqualTo(_head.position).Using(kMm));
        }
    }
}
