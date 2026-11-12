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

        // From the head (m): a drift forward and down, 0.2 mm a frame; a rise straight up through the spine's furthest
        // reach, 0.1 mm a frame (sitting up tall); and a slouch, the head coming nearer the hips.
        [TestCase(0f, -0.01f, 0.03f, 0f, -0.0001f, 0.0002f, 150, TestName = "Reach_TargetDriftsSmoothly_SpineTurnsSmoothly")]
        [TestCase(0f, -0.02f, 0.005f, 0f, 0.0001f, 0f, 400, TestName = "Reach_TargetRisesThroughFullReach_SpineTurnsSmoothly")]
        [TestCase(0f, 0f, 0.02f, 0f, -0.0002f, 0.0001f, 400, TestName = "Reach_TargetSlouches_SpineTurnsSmoothly")]
        public void Reach_TargetMovesSmoothly_SpineTurnsSmoothly(float x0, float y0, float z0, float dx, float dy, float dz, int frames)
        {
            var rig = SeatedRig();
            var chain = new[] { rig.Pelvis, rig.Spine[0], rig.Spine[1], rig.Spine[2] };
            // Reach turns the hips back as the pelvis tips, so they are re-posed too.
            var posed = new[] { rig.Pelvis, rig.LeftHip, rig.RightHip, rig.Spine[0], rig.Spine[1], rig.Spine[2], rig.Spine[3] };
            var startLocal = System.Array.ConvertAll(posed, t => t.localRotation);
            var startRoot = rig.Root.position;
            var head = rig.Head.position;
            var turns = new float[frames][];
            Quaternion[] first = null;

            // As a seated body is re-posed from the worker's pose each frame: the same start, the target a step on.
            for (var f = 0; f < turns.Length; f++)
            {
                for (var i = 0; i < posed.Length; i++) posed[i].localRotation = startLocal[i];
                rig.Root.position = startRoot;
                SpineLean.Reach(rig.Root, rig.Pelvis, rig.LeftHip, rig.RightHip, rig.Spine, rig.Head,
                    head + new Vector3(x0 + dx * f, y0 + dy * f, z0 + dz * f));
                var now = System.Array.ConvertAll(chain, t => t.rotation);
                first ??= now;
                turns[f] = new float[chain.Length];
                for (var j = 0; j < chain.Length; j++) turns[f][j] = Turn(first[j], now[j]);
            }

            // Each joint's change of step from one frame to the next (degrees). Stopping the rounds within 1 mm made
            // a joint stand still and then turn ~0.2 degrees in one frame, ten times its steady step. Matching the
            // head's distance from the hips exactly swung the curl and the pelvis by degrees a frame near a straight
            // spine, where curling hardly changes that distance.
            var worst = 0f;
            for (var f = 1; f + 1 < turns.Length; f++)
                for (var j = 0; j < chain.Length; j++)
                    worst = Mathf.Max(worst, Mathf.Abs(turns[f + 1][j] - 2f * turns[f][j] + turns[f - 1][j]));
            Assert.That(worst, Is.LessThan(0.1f));
        }

        // Degrees from a to b, from the relative rotation's vector part: exact for small angles, where Quaternion.Angle
        // (an arccos near 1) is only good to ~0.03 degrees.
        static float Turn(Quaternion a, Quaternion b)
        {
            var r = Quaternion.Inverse(a) * b;
            return 2f * Mathf.Asin(Mathf.Min(1f, new Vector3(r.x, r.y, r.z).magnitude)) * Mathf.Rad2Deg;
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
