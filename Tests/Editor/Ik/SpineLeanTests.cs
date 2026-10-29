using NUnit.Framework;
using UnityEngine;
using VrChat.Ik;

namespace VrChat.Testing
{
    [TestFixture]
    public class SpineLeanTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // Leaning forward and down, leaning to the side, leaning back (on the arc about the hips), slumping, and leaning
        // 30 degrees forward while slumping a little (m, from the head).
        [TestCase(0f, -0.05f, 0.15f)]
        [TestCase(0.1f, -0.02f, 0.1f)]
        [TestCase(0f, -0.01f, -0.1f)]
        [TestCase(0f, -0.04f, 0f)]
        [TestCase(0f, -0.12f, 0.35f)]
        public void Reach_TargetWithinReach_PutsTheHeadOnItWithTheThighsStill(float x, float y, float z)
        {
            var rig = SeatedRig();
            var knees = (rig.LeftKnee.position, rig.RightKnee.position);
            var lengths = BoneLengths(rig);
            var target = rig.Head.position + new Vector3(x, y, z);

            SpineLean.Reach(rig.Root, rig.Pelvis, rig.LeftHip, rig.RightHip, rig.Spine, rig.Head, target);

            Assert.That((rig.Head.position - target).magnitude, Is.LessThan(0.005f), "reached");
            Assert.That((rig.LeftKnee.position - knees.Item1).magnitude, Is.LessThan(1e-5f), "left thigh still");
            Assert.That((rig.RightKnee.position - knees.Item2).magnitude, Is.LessThan(1e-5f), "right thigh still");
            Assert.That(BoneLengths(rig), Is.EqualTo(lengths).Within(1e-5f), "bone lengths");
        }

        [Test]
        public void Reach_TargetAtTheHead_LeavesThePose()
        {
            var rig = SeatedRig();
            var before = Poses(rig);

            SpineLean.Reach(rig.Root, rig.Pelvis, rig.LeftHip, rig.RightHip, rig.Spine, rig.Head, rig.Head.position);

            var after = Poses(rig);
            for (var i = 0; i < before.Length; i++)
                Assert.That(Quaternion.Angle(before[i], after[i]), Is.LessThan(0.01f), $"joint {i}");
        }

        [Test]
        public void Reach_TargetOutOfReach_PutsTheHeadOnTheLineFromTheHipsToIt()
        {
            var rig = SeatedRig();
            var hips = (rig.LeftHip.position + rig.RightHip.position) * 0.5f;
            var target = hips + new Vector3(0f, 2f, 1f);

            SpineLean.Reach(rig.Root, rig.Pelvis, rig.LeftHip, rig.RightHip, rig.Spine, rig.Head, target);

            Assert.That(Vector3.Angle(rig.Head.position - hips, target - hips), Is.LessThan(0.5f));
        }

        sealed class Rig
        {
            public Transform Root, Pelvis, LeftHip, RightHip, LeftKnee, RightKnee, Head;
            public Transform[] Spine;
        }

        // A seated body about SMPL-X's, facing +z: the pelvis 0.6 m up, hips 11 cm apart with the thighs forward, and
        // spine1, spine2, spine3, the neck and the head 0.68 m above the pelvis, the spine bent slightly back.
        Rig SeatedRig()
        {
            _root = new GameObject("Root");
            var rig = new Rig { Root = _root.transform };
            rig.Pelvis = Child("Pelvis", rig.Root, new Vector3(0f, 0.6f, 0f));
            rig.LeftHip = Child("Left Hip", rig.Pelvis, new Vector3(-0.057f, -0.09f, -0.02f));
            rig.RightHip = Child("Right Hip", rig.Pelvis, new Vector3(0.057f, -0.09f, -0.02f));
            rig.LeftKnee = Child("Left Knee", rig.LeftHip, new Vector3(-0.07f, 0f, 0.39f));
            rig.RightKnee = Child("Right Knee", rig.RightHip, new Vector3(0.05f, 0f, 0.39f));
            var offsets = new[]
            {
                new Vector3(0f, 0.11f, -0.03f), new Vector3(0f, 0.13f, 0.01f), new Vector3(0f, 0.06f, 0.01f),
                new Vector3(0f, 0.22f, -0.01f), new Vector3(0f, 0.16f, 0.02f),
            };
            rig.Spine = new Transform[4];
            var parent = rig.Pelvis;
            for (var i = 0; i < rig.Spine.Length; i++) parent = rig.Spine[i] = Child($"Spine {i}", parent, offsets[i]);
            rig.Head = Child("Head", parent, offsets[^1]);
            return rig;
        }

        static Transform Child(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = localPosition;
            return t;
        }

        static float[] BoneLengths(Rig rig)
        {
            var chain = new[] { rig.Pelvis, rig.Spine[0], rig.Spine[1], rig.Spine[2], rig.Spine[3], rig.Head };
            var lengths = new float[chain.Length + 1];
            for (var i = 0; i + 1 < chain.Length; i++) lengths[i] = (chain[i + 1].position - chain[i].position).magnitude;
            lengths[^2] = (rig.LeftHip.position - rig.Pelvis.position).magnitude;
            lengths[^1] = (rig.RightKnee.position - rig.RightHip.position).magnitude;
            return lengths;
        }

        static Quaternion[] Poses(Rig rig) => new[]
        {
            rig.Pelvis.rotation, rig.LeftHip.rotation, rig.RightHip.rotation,
            rig.Spine[0].rotation, rig.Spine[1].rotation, rig.Spine[2].rotation, rig.Spine[3].rotation,
        };
    }
}
