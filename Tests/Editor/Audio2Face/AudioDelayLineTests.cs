using System;
using System.Collections.Generic;
using NUnit.Framework;
using VrChat.Audio2Face;

namespace VrChat.Testing.Audio2Face
{
    [TestFixture]
    public class AudioDelayLineTests
    {
        static float[] Ramp(int samples)
        {
            var ramp = new float[samples];
            for (var i = 0; i < samples; i++) ramp[i] = i + 1;
            return ramp;
        }

        static float[] Run(AudioDelayLine sut, float[] interleaved, int blockSamples)
        {
            var output = new List<float>();
            for (var start = 0; start < interleaved.Length; start += blockSamples)
            {
                var block = interleaved.AsSpan(start, Math.Min(blockSamples, interleaved.Length - start)).ToArray();
                sut.Process(block);
                output.AddRange(block);
            }
            return output.ToArray();
        }

        [Test]
        public void Process_Impulse_ComesOutDelayFramesLaterOnItsChannel()
        {
            var sut = new AudioDelayLine(5, 2);
            var input = new float[2 * 20];
            input[1] = 1f; // frame 0, channel 1

            var actual = Run(sut, input, input.Length);

            Assert.That(actual[2 * 5 + 1], Is.EqualTo(1f));
        }

        [Test]
        public void Process_FirstDelayFrames_AreSilent()
        {
            var sut = new AudioDelayLine(5, 2);

            var actual = Run(sut, Ramp(2 * 20), 40);

            Assert.That(actual[..10], Is.All.EqualTo(0f));
        }

        [Test]
        public void Process_SplitIntoBlocks_MatchesOneBlock()
        {
            var input = Ramp(2 * 50);
            var expected = Run(new AudioDelayLine(7, 2), input, input.Length);
            var sut = new AudioDelayLine(7, 2);

            var actual = Run(sut, input, 6);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Process_ZeroDelay_LeavesTheSamplesUnchanged()
        {
            var input = Ramp(2 * 10);
            var sut = new AudioDelayLine(0, 2);

            var actual = Run(sut, input, input.Length);

            Assert.That(actual, Is.EqualTo(input));
        }
    }
}
