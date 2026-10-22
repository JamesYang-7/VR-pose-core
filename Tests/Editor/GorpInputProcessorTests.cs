using NUnit.Framework;

namespace VrChat.Testing
{
    /// <summary>
    /// The GORP input processing (RPM-README §4) against the export: each reference sequence's raw device stream
    /// must come out as the model's stored frames under RPM's rule (zeroing), as RPM-README §6 checks; and the
    /// hold and reappearance options on synthetic frames.
    /// </summary>
    [TestFixture]
    public class GorpInputProcessorTests
    {
        const int kRawFrame = GorpDevice.Points * GorpDevice.RawWidth;

        // Head and right tracked, left reporting no confidence unless leftValid; poses are arbitrary but valid.
        static float[] RawFrame(float x, float leftValid = 0f) => new[]
        {
            x, 1.6f, 0.1f, 1f, 0f, 0f, 0f, 1f,
            x - 0.2f, 1.0f, 0.3f, 0.9f, 0.1f, 0.3f, 0.2f, leftValid,
            x + 0.2f, 1.1f, 0.3f, 0.8f, -0.2f, 0.4f, 0.1f, 1f,
        };

        [TestCase("s06_fruitninja-q3-controllers_000")] // dropouts: zeroed, and no reappearance rule
        [TestCase("s11_beatsaber-q3-controllers_900")]
        [TestCase("s26_fruitninja-q3-controllers_000")]
        public void Push_RawStreamUnderRpmRule_ReproducesRpmModelFrames(string name)
        {
            ModelLocalFiles.RequireRpm();
            AssertReproduces(ModelExport.Rpm.LoadSequence(name), new GorpInputProcessor(HandMasking.Zero, reappearStaticFrames: 0));
        }

        static void AssertReproduces(GorpSequence seq, GorpInputProcessor sut)
        {
            var actual = new float[PoseLayout.InputWidth];
            sut.Push(seq.SparseRaw, 0, actual);

            float worst = 0f;
            int worstFrame = -1;
            for (int t = 0; t < seq.FrameCount; t++)
            {
                sut.Push(seq.SparseRaw, (t + 1) * kRawFrame, actual);
                for (int k = 0; k < actual.Length; k++)
                {
                    float diff = System.Math.Abs(actual[k] - seq.SparseCtx[t * PoseLayout.InputWidth + k]);
                    if (diff > worst) (worst, worstFrame) = (diff, t);
                }
            }

            Assert.That(worst, Is.LessThan(1e-5f), $"worst difference, at frame {worstFrame}");
        }

        [Test]
        public void Push_FirstFrame_ReturnsFalse()
        {
            var sut = new GorpInputProcessor(HandMasking.Hold, reappearStaticFrames: 1);

            bool actual = sut.Push(RawFrame(0f), 0, new float[PoseLayout.InputWidth]);

            Assert.That(actual, Is.False);
        }

        [Test]
        public void Push_HandNeverTracked_ZeroesEveryNumberOfThatHand()
        {
            var sut = new GorpInputProcessor(HandMasking.Hold, reappearStaticFrames: 1);
            var actual = new float[PoseLayout.InputWidth];
            sut.Push(RawFrame(0f), 0, actual);

            sut.Push(RawFrame(0.01f), 0, actual);

            Assert.That(actual[6..12], Is.EqualTo(new float[6]), "rotation");
            Assert.That(actual[24..30], Is.EqualTo(new float[6]), "rotation velocity");
            Assert.That(actual[39..42], Is.EqualTo(new float[3]), "position");
            Assert.That(actual[48..51], Is.EqualTo(new float[3]), "position velocity");
        }

        [Test]
        public void Push_HoldMaskingHandLost_HoldsItsLastTrackedPose()
        {
            var sut = new GorpInputProcessor(HandMasking.Hold, reappearStaticFrames: 1);
            var tracked = new float[PoseLayout.InputWidth];
            var actual = new float[PoseLayout.InputWidth];
            sut.Push(RawFrame(0f, leftValid: 1f), 0, tracked);
            sut.Push(RawFrame(0.01f, leftValid: 1f), 0, tracked);

            sut.Push(RawFrame(0.02f), 0, actual);

            Assert.That(actual[39..42], Is.EqualTo(tracked[39..42]), "position held");
            Assert.That(actual[24..30], Is.EqualTo(new float[] { 1, 0, 0, 0, 1, 0 }), "identity rotation velocity");
        }

        [Test]
        public void Push_ZeroMaskingHandLost_ZeroesEveryNumberOfThatHand()
        {
            var sut = new GorpInputProcessor(HandMasking.Zero, reappearStaticFrames: 0);
            var actual = new float[PoseLayout.InputWidth];
            sut.Push(RawFrame(0f, leftValid: 1f), 0, actual);
            sut.Push(RawFrame(0.01f, leftValid: 1f), 0, actual);

            sut.Push(RawFrame(0.02f), 0, actual);

            Assert.That(actual[6..12], Is.EqualTo(new float[6]), "rotation");
            Assert.That(actual[24..30], Is.EqualTo(new float[6]), "rotation velocity");
            Assert.That(actual[39..42], Is.EqualTo(new float[3]), "position");
            Assert.That(actual[48..51], Is.EqualTo(new float[3]), "position velocity");
        }

        [Test]
        public void Push_ZeroMaskingHandReappears_KeepsItsVelocityAgainstTheUntrackedFrame()
        {
            var sut = new GorpInputProcessor(HandMasking.Zero, reappearStaticFrames: 0);
            var actual = new float[PoseLayout.InputWidth];
            sut.Push(RawFrame(0f, leftValid: 1f), 0, actual);
            sut.Push(RawFrame(0.01f), 0, actual);

            sut.Push(RawFrame(0.03f, leftValid: 1f), 0, actual);

            Assert.That(actual[48], Is.EqualTo(0.02f).Within(1e-6f), "x velocity against the reported, untracked pose");
        }

        [Test]
        public void Push_ThresholdZero_NeverMasks()
        {
            var sut = new GorpInputProcessor(HandMasking.Zero, reappearStaticFrames: 0, threshold: 0f);
            var actual = new float[PoseLayout.InputWidth];
            sut.Push(RawFrame(0f), 0, actual);

            sut.Push(RawFrame(0.01f), 0, actual);

            Assert.That(actual[48], Is.EqualTo(0.01f).Within(1e-6f), "left x velocity");
        }
    }
}
