using System;
using NUnit.Framework;
using VrChat.Audio2Face;

namespace VrChat.Testing.Audio2Face
{
    [TestFixture]
    public class FrameTimelineTests
    {
        static FrameTimeline CreateSystemUnderTest(int capacity, params (long time, float value)[] frames)
        {
            var sut = new FrameTimeline(2, capacity);
            foreach (var (time, value) in frames) sut.Add(time, new[] { value, -value });
            return sut;
        }

        static float[] Sample(FrameTimeline sut, long position)
        {
            var output = new float[2];
            sut.Sample(position, output);
            return output;
        }

        [Test]
        public void Sample_NoFrames_ReturnsFalse()
        {
            var sut = CreateSystemUnderTest(8);

            var actual = sut.Sample(0, new float[2]);

            Assert.That(actual, Is.False);
        }

        [Test]
        public void Sample_BetweenTwoFrames_InterpolatesLinearly()
        {
            var sut = CreateSystemUnderTest(8, (100, 0f), (200, 1f));

            var actual = Sample(sut, 125);

            Assert.That(actual, Is.EqualTo(new[] { 0.25f, -0.25f }).Within(1e-6f));
        }

        [Test]
        public void Sample_AtAFrameTimestamp_ReturnsThatFrame()
        {
            var sut = CreateSystemUnderTest(8, (100, 0f), (200, 1f), (300, 0.5f));

            var actual = Sample(sut, 200);

            Assert.That(actual, Is.EqualTo(new[] { 1f, -1f }));
        }

        [Test]
        public void Sample_BeforeTheFirstFrame_ReturnsTheFirstFrame()
        {
            var sut = CreateSystemUnderTest(8, (100, 0.3f), (200, 1f));

            var actual = Sample(sut, 50);

            Assert.That(actual, Is.EqualTo(new[] { 0.3f, -0.3f }));
        }

        [Test]
        public void Sample_AfterTheLastFrame_HoldsTheLastFrame()
        {
            var sut = CreateSystemUnderTest(8, (100, 0f), (200, 0.7f));

            var actual = Sample(sut, 10000);

            Assert.That(actual, Is.EqualTo(new[] { 0.7f, -0.7f }));
        }

        [Test]
        public void Sample_PositionPastSomeFrames_DropsTheFramesBeforeTheBracketingOne()
        {
            var sut = CreateSystemUnderTest(8, (100, 0f), (200, 0f), (300, 0f), (400, 0f));

            sut.Sample(250, new float[2]);

            Assert.That(sut.Count, Is.EqualTo(3));
        }

        [Test]
        public void Add_WhenFull_DropsTheOldestFrame()
        {
            var sut = CreateSystemUnderTest(2, (100, 0.1f), (200, 0.2f), (300, 0.3f));

            var actual = Sample(sut, 100);

            Assert.That(actual, Is.EqualTo(new[] { 0.2f, -0.2f }));
        }

        [Test]
        public void Add_TimestampNotAfterTheNewest_ThrowsArgumentException()
        {
            var sut = CreateSystemUnderTest(8, (100, 0f));

            Assert.That(() => sut.Add(100, new[] { 0f, 0f }), Throws.TypeOf<ArgumentException>());
        }
    }
}
