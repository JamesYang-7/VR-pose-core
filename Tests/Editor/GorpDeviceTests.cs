using MyUtils;
using NUnit.Framework;
using UnityEngine;

namespace VrChat.Testing
{
    /// <summary>
    /// GORP device conversion: README 4.2 on its worked example, and the Unity ↔ raw mapping, which has to give
    /// the head joint frame of the project's Blender convention and land back in Unity through
    /// <see cref="GorpPose.ToUnity"/>.
    /// </summary>
    [TestFixture]
    public class GorpDeviceTests
    {
        static double[] Column(double[] r, int c) => new[] { r[c], r[3 + c], r[6 + c] };

        static Vector3 ToVector(double[] v) => new((float)v[0], (float)v[1], (float)v[2]);

        // Rodrigues, row-major, for an axis-angle vector (radians).
        static double[] AxisAngleMatrix(Vector3 aa)
        {
            double th = aa.magnitude;
            var k = aa / (float)th;
            double c = System.Math.Cos(th), s = System.Math.Sin(th), t = 1 - c;
            return new[]
            {
                t * k.x * k.x + c,       t * k.x * k.y - s * k.z, t * k.x * k.z + s * k.y,
                t * k.x * k.y + s * k.z, t * k.y * k.y + c,       t * k.y * k.z - s * k.x,
                t * k.x * k.z - s * k.y, t * k.y * k.z + s * k.x, t * k.z * k.z + c,
            };
        }

        static double AngleDegrees(double[] a, double[] b)
        {
            double trace = 0;
            for (int i = 0; i < 9; i++) trace += a[i] * b[i]; // tr(aᵀ·b)
            return System.Math.Acos(System.Math.Clamp((trace - 1) / 2, -1, 1)) * 180 / System.Math.PI;
        }

        [Test]
        public void ToSmpl_WorkedExampleHeadRow_MatchesTheReadme()
        {
            // RPM-README 4.5: s26 raw row 1, head.
            var raw = new[] { -0.981341f, 1.575140f, 0.981141f, 0.807310f, 0.048710f, -0.587223f, -0.032369f, 1f };
            var position = new double[3];
            var actual = new double[9];

            GorpDevice.ToSmpl(raw, 0, GorpDevice.Head, position, actual);

            Assert.That(position, Is.EqualTo(new[] { -0.896106, -1.025318, 1.562036 }).Within(2e-6), "position");
            Assert.That(actual, Is.EqualTo(new[]
            {
                -0.308243, -0.109471, -0.944988,
                -0.951295, 0.040632, 0.305593,
                0.004943, 0.993159, -0.116664,
            }).Within(2e-6), "rotation");
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(-20f, 35f, 0f)]
        [TestCase(10f, 170f, -25f)]
        [TestCase(80f, -60f, 45f)]
        public void FromUnity_AnyHeadsetRotation_GivesTheHeadJointFrameOfTheBlenderConvention(float x, float y, float z)
        {
            var rotation = Quaternion.Euler(x, y, z);
            var expected = AxisAngleMatrix(MathUtils.GlobalRotationUnityToBlender(rotation));
            var raw = new float[GorpDevice.RawWidth];
            var actual = new double[9];

            GorpDevice.FromUnity(new Vector3(0.3f, 1.6f, -0.2f), rotation, 1f, raw, 0);
            GorpDevice.ToSmpl(raw, 0, GorpDevice.Head, new double[3], actual);

            Assert.That(AngleDegrees(actual, expected), Is.LessThan(0.1));
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(10f, 170f, -25f)]
        [TestCase(80f, -60f, 45f)]
        public void ToUnity_PoseWrittenByFromUnity_GivesThatPoseBack(float x, float y, float z)
        {
            var expected = new Pose(new Vector3(0.3f, 1.6f, -0.2f), Quaternion.Euler(x, y, z));
            var raw = new float[GorpDevice.RawWidth];
            GorpDevice.FromUnity(expected.position, expected.rotation, 1f, raw, 0);

            var actual = GorpDevice.ToUnity(raw, 0);

            Assert.That((actual.position - expected.position).magnitude, Is.LessThan(1e-6f), "position (m)");
            Assert.That(Quaternion.Angle(actual.rotation, expected.rotation), Is.LessThan(0.01f), "rotation (deg)");
        }

        [Test]
        public void ToUnity_RawStreamOfS06ThroughFromUnity_ReproducesItsModelFrames()
        {
            // What a replay into Unity transforms feeds the live workers; s06 has controller dropouts.
            ModelLocalFiles.RequireRpm();
            var seq = ModelExport.Rpm.LoadSequence("s06_fruitninja-q3-controllers_000");
            var roundTrip = new float[seq.SparseRaw.Length];
            for (int o = 0; o < roundTrip.Length; o += GorpDevice.RawWidth)
            {
                var pose = GorpDevice.ToUnity(seq.SparseRaw, o);
                GorpDevice.FromUnity(pose.position, pose.rotation, seq.SparseRaw[o + 7], roundTrip, o);
            }
            var processor = new GorpInputProcessor(HandMasking.Zero, reappearStaticFrames: 0);
            var frame = new float[PoseLayout.InputWidth];
            const int rawFrame = GorpDevice.Points * GorpDevice.RawWidth;
            processor.Push(roundTrip, 0, frame);

            float worst = 0f;
            for (int t = 0; t < seq.FrameCount; t++)
            {
                processor.Push(roundTrip, (t + 1) * rawFrame, frame);
                for (int k = 0; k < frame.Length; k++)
                    worst = Mathf.Max(worst, Mathf.Abs(frame[k] - seq.SparseCtx[t * PoseLayout.InputWidth + k]));
            }

            Assert.That(worst, Is.LessThan(1e-5f));
        }

        [Test]
        public void FromUnity_LevelHeadsetLookingAlongUnityForward_PutsHeadJointBehindAndBelowFacingForward()
        {
            var raw = new float[GorpDevice.RawWidth];
            var position = new double[3];
            var rotation = new double[9];

            GorpDevice.FromUnity(new Vector3(0f, 1.7f, 0f), Quaternion.identity, 1f, raw, 0);
            GorpDevice.ToSmpl(raw, 0, GorpDevice.Head, position, rotation);

            // GORP's head offset: the device sits 9.3 cm ahead of, 2.4 cm above and 1.6 cm right of the joint.
            var joint = GorpPose.ToUnity(ToVector(position));
            Assert.That(joint.x, Is.EqualTo(-0.0157f).Within(1e-3f), "left of the headset");
            Assert.That(joint.y, Is.EqualTo(1.7f - 0.0241f).Within(1e-3f), "below the headset");
            Assert.That(joint.z, Is.EqualTo(-0.0925f).Within(1e-3f), "behind the headset");
            var forward = GorpPose.ToUnity(ToVector(Column(rotation, 2))); // SMPL faces its joints' +z
            Assert.That(Vector3.Angle(forward, Vector3.forward), Is.LessThan(0.1f), "facing (deg)");
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(10f, 170f, -25f)]
        [TestCase(80f, -60f, 45f)]
        public void ToGrip_PoseMadeByFromGrip_GivesThatGripPoseBack(float x, float y, float z)
        {
            var expected = new Pose(new Vector3(0.3f, 1.2f, -0.2f), Quaternion.Euler(x, y, z));
            var correction = Quaternion.Euler(GorpDevice.GripCorrectionEuler);
            var controller = GorpDevice.FromGrip(expected, GorpDevice.GripOffset, correction);

            var actual = GorpDevice.ToGrip(controller, GorpDevice.GripOffset, correction);

            Assert.That((actual.position - expected.position).magnitude, Is.LessThan(1e-6f), "position (m)");
            Assert.That(Quaternion.Angle(actual.rotation, expected.rotation), Is.LessThan(0.01f), "rotation (deg)");
        }

        // Wrist joints measured on a Quest over Link (2026-10-21): least-squares pivot fits of the grip pose while
        // the hand turned about a still forearm, in the grip's own Unity axes (cm). The plain grip pose with the old
        // (90,0,0) correction put them 7.1 (right) and 8.4 cm (left) away.
        [TestCase(GorpDevice.Left, -5.1f, 4.3f, -3.9f)]
        [TestCase(GorpDevice.Right, 3.7f, 5.4f, -3.5f)]
        public void FromGrip_DefaultOffsetAndCorrection_PutsTheWristJointWithin3CmOfTheMeasuredPivot(int device, float x, float y, float z)
        {
            var grip = new Pose(new Vector3(0.3f, 1.2f, 0.4f), Quaternion.Euler(20f, -30f, 10f));
            var controller = GorpDevice.FromGrip(grip, GorpDevice.GripOffset, Quaternion.Euler(GorpDevice.GripCorrectionEuler));
            var raw = new float[GorpDevice.RawWidth];
            var position = new double[3];
            GorpDevice.FromUnity(controller.position, controller.rotation, 1f, raw, 0);
            GorpDevice.ToSmpl(raw, 0, device, position, new double[9]);

            var wrist = Quaternion.Inverse(grip.rotation) * (GorpPose.ToUnity(ToVector(position)) - grip.position);

            Assert.That((wrist - new Vector3(x, y, z) / 100f).magnitude, Is.LessThan(0.03f));
        }
    }
}
