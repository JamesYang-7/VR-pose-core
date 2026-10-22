using NUnit.Framework;
using UnityEngine;
using VrChat.Retarget;

namespace VrChat.Testing.Retarget
{
    /// <summary>
    /// A segment along +Y with a distal joint 1 m down it, and CC5-style nested twist bones:
    /// one at the segment's joint (t = 0) and one halfway along (t = 0.5), parented under the first.
    /// </summary>
    [TestFixture]
    public class TwistDistributorTests
    {
        GameObject _root;
        Transform _segment, _distal, _twistAtJoint, _twistMid;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TwistDistributorTests");
            _segment = NewBone("Segment", _root.transform, Vector3.zero);
            _distal = NewBone("Distal", _segment, Vector3.up);
            _twistAtJoint = NewBone("Twist01", _segment, Vector3.zero);
            _twistMid = NewBone("Twist02", _twistAtJoint, new Vector3(0f, 0.5f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        static Transform NewBone(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        TwistDistributor NewDistributor() => new(_segment, _distal, new[] { _twistAtJoint, _twistMid });

        [Test]
        public void Apply_SegmentRolled_TwistBoneAtJointCancelsTheRoll()
        {
            var sut = NewDistributor();
            _segment.localRotation = Quaternion.AngleAxis(60f, Vector3.up);

            sut.Apply();

            Assert.That(Quaternion.Angle(_twistAtJoint.rotation, Quaternion.identity), Is.LessThan(1e-3f));
        }

        [Test]
        public void Apply_SegmentRolled_MidTwistBoneKeepsHalfTheRoll()
        {
            var sut = NewDistributor();
            _segment.localRotation = Quaternion.AngleAxis(60f, Vector3.up);

            sut.Apply();

            Assert.That(Quaternion.Angle(_twistMid.rotation, Quaternion.AngleAxis(30f, Vector3.up)), Is.LessThan(1e-3f));
        }

        [Test]
        public void Apply_DistalRolled_MidTwistBoneTakesHalfTheRoll()
        {
            var sut = NewDistributor();
            _distal.localRotation = Quaternion.AngleAxis(80f, Vector3.up);

            sut.Apply();

            Assert.That(Quaternion.Angle(_twistMid.rotation, Quaternion.AngleAxis(40f, Vector3.up)), Is.LessThan(1e-3f));
        }

        [Test]
        public void Apply_SegmentSwungWithoutRoll_TwistBonesFollowTheSegment()
        {
            var sut = NewDistributor();
            _segment.localRotation = Quaternion.AngleAxis(50f, Vector3.right);

            sut.Apply();

            Assert.That(Quaternion.Angle(_twistMid.rotation, _segment.rotation), Is.LessThan(1e-3f));
        }
    }
}
