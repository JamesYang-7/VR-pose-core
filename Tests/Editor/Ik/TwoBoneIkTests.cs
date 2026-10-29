using NUnit.Framework;
using UnityEngine;
using VrChat.Ik;

namespace VrChat.Testing
{
    [TestFixture]
    public class TwoBoneIkTests
    {
        // About SMPL-X's left arm: upper arm 0.27 m, forearm 0.27 m.
        const float Upper = 0.27f;
        const float Lower = 0.27f;
        static readonly Vector3 DownBackOut = new(-0.4f, -1f, -0.5f);

        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void Place_TargetWithinReach_PutsTheEndOnItWithTheBoneLengths()
        {
            var target = new Vector3(-0.2f, -0.25f, 0.2f);

            var (mid, end) = TwoBoneIk.Place(Vector3.zero, target, Upper, Lower, DownBackOut);

            Assert.That((end - target).magnitude, Is.LessThan(1e-5f), "end on the target");
            Assert.That(mid.magnitude, Is.EqualTo(Upper).Within(1e-5f), "upper length");
            Assert.That((end - mid).magnitude, Is.EqualTo(Lower).Within(1e-5f), "lower length");
        }

        [Test]
        public void Place_TargetWithinReach_BendsTheMiddleJointTowardsTheHint()
        {
            var target = new Vector3(-0.2f, -0.25f, 0.2f);
            var along = target.normalized;
            var hintAcross = DownBackOut - Vector3.Dot(DownBackOut, along) * along;

            var (mid, _) = TwoBoneIk.Place(Vector3.zero, target, Upper, Lower, DownBackOut);

            var midAcross = mid - Vector3.Dot(mid, along) * along;
            Assert.That(Vector3.Angle(midAcross, hintAcross), Is.LessThan(0.01f));
        }

        [Test]
        public void Place_TargetOutOfReach_StretchesTheLimbAlongTheLineToIt()
        {
            var target = new Vector3(-1f, 0.2f, 0.5f);

            var (mid, end) = TwoBoneIk.Place(Vector3.zero, target, Upper, Lower, DownBackOut);

            Assert.That(Vector3.Angle(end, target), Is.LessThan(1e-3f), "end on the line");
            Assert.That(end.magnitude, Is.EqualTo(Upper + Lower).Within(0.001f), "end at the bones' reach");
            Assert.That(Vector3.Angle(mid, target), Is.LessThan(5f), "limb all but straight");
        }

        [Test]
        public void Place_TargetNearerThanTheBonesAllow_KeepsTheEndTheirDifferenceAway()
        {
            var target = new Vector3(0f, -0.05f, 0f);

            var (_, end) = TwoBoneIk.Place(Vector3.zero, target, 0.3f, 0.2f, DownBackOut);

            Assert.That((end - new Vector3(0f, -0.1f, 0f)).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void Place_HintAlongTheLimb_StillBendsWithTheBoneLengths()
        {
            var target = new Vector3(0f, -0.4f, 0f);

            var (mid, end) = TwoBoneIk.Place(Vector3.zero, target, Upper, Lower, Vector3.down);

            Assert.That((end - target).magnitude, Is.LessThan(1e-5f), "end on the target");
            Assert.That(mid.magnitude, Is.EqualTo(Upper).Within(1e-5f), "upper length");
            Assert.That((end - mid).magnitude, Is.EqualTo(Lower).Within(1e-5f), "lower length");
        }

        [TestCase(-0.2f, -0.25f, 0.2f)] // hand in front, at the waist
        [TestCase(-0.1f, 0.35f, 0.15f)] // overhead
        [TestCase(-0.45f, 0.05f, 0f)] // out to the side
        public void Solve_TargetWithinReach_PutsTheEndWherePlaceDoes(float x, float y, float z)
        {
            var (upper, mid, end) = TPoseArm();
            var target = new Vector3(x, y, z);
            var bend = Vector3.zero;

            TwoBoneIk.Solve(upper, mid, end, target, DownBackOut, Vector3.forward, ref bend);

            var (expectedMid, expectedEnd) = TwoBoneIk.Place(Vector3.zero, target, Upper, Lower, DownBackOut);
            Assert.That((end.position - expectedEnd).magnitude, Is.LessThan(1e-4f), "end");
            Assert.That((mid.position - expectedMid).magnitude, Is.LessThan(1e-4f), "middle joint");
        }

        [TestCase(-0.2f, -0.25f, 0.2f)]
        [TestCase(-0.1f, 0.35f, 0.15f)]
        public void Solve_FromAStraightRestLimb_BendsTheMiddleJointAboutItsRestHinge(float x, float y, float z)
        {
            var (upper, mid, end) = TPoseArm();
            var bend = Vector3.zero;
            // Out along -x, flexing forward: the hinge is the vertical.
            var hinge = Vector3.Cross(Vector3.left, Vector3.forward);

            TwoBoneIk.Solve(upper, mid, end, new Vector3(x, y, z), DownBackOut, Vector3.forward, ref bend);

            mid.localRotation.ToAngleAxis(out _, out var axis);
            Assert.That(Mathf.Abs(Vector3.Dot(axis, hinge)), Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Solve_HintThatLinesUpWithTheLimb_KeepsTheLastBendSide()
        {
            var (upper, mid, end) = TPoseArm();
            var target = new Vector3(-0.15f, -0.4f, 0.05f);
            var bend = Vector3.zero;
            TwoBoneIk.Solve(upper, mid, end, target, DownBackOut, Vector3.forward, ref bend);
            var before = bend;

            TwoBoneIk.Solve(upper, mid, end, target, target, upper.rotation * Vector3.forward, ref bend);

            Assert.That(Vector3.Angle(bend, before), Is.LessThan(1f));
        }

        // A straight left arm in a T-pose: shoulder at the origin, elbow and wrist out along -x, every rotation identity.
        (Transform upper, Transform mid, Transform end) TPoseArm()
        {
            _root = new GameObject("Shoulder");
            var mid = Child("Elbow", _root.transform, new Vector3(-Upper, 0f, 0f));
            var end = Child("Wrist", mid, new Vector3(-Lower, 0f, 0f));
            return (_root.transform, mid, end);
        }

        static Transform Child(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = localPosition;
            return t;
        }
    }
}
