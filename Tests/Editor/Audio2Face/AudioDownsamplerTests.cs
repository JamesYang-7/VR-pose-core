using System;
using System.Collections.Generic;
using NUnit.Framework;
using VrChat.Audio2Face;

namespace VrChat.Testing.Audio2Face
{
    [TestFixture]
    public class AudioDownsamplerTests
    {
        static float[] Tone(int rate, double hz, int frames, float amplitude = 0.5f)
        {
            var samples = new float[frames];
            for (var i = 0; i < frames; i++) samples[i] = amplitude * (float)Math.Sin(2.0 * Math.PI * hz * i / rate);
            return samples;
        }

        static float[] Constant(int frames, float value)
        {
            var samples = new float[frames];
            Array.Fill(samples, value);
            return samples;
        }

        static float[] StereoLeftOnly(int frames, float value)
        {
            var interleaved = new float[2 * frames];
            for (var i = 0; i < interleaved.Length; i += 2) interleaved[i] = value;
            return interleaved;
        }

        static float[] Run(AudioDownsampler sut, float[] interleaved, int channels, int blockFrames)
        {
            var output = new List<float>();
            var buffer = new float[sut.MaxOutputCount(blockFrames)];
            for (var start = 0; start < interleaved.Length; start += blockFrames * channels)
            {
                var length = Math.Min(blockFrames * channels, interleaved.Length - start);
                var count = sut.Process(interleaved.AsSpan(start, length), channels, buffer);
                output.AddRange(buffer.AsSpan(0, count).ToArray());
            }
            return output.ToArray();
        }

        static double Rms(float[] samples, int from, int to)
        {
            var sum = 0.0;
            for (var i = from; i < to; i++) sum += samples[i] * samples[i];
            return Math.Sqrt(sum / (to - from));
        }

        static int ArgMax(float[] samples)
        {
            var best = 0;
            for (var i = 1; i < samples.Length; i++)
                if (samples[i] > samples[best]) best = i;
            return best;
        }

        [Test]
        public void Process_ConstantSignal_OutputsTheSameLevel()
        {
            var sut = new AudioDownsampler(48000, 16000);

            var actual = Run(sut, Constant(4800, 0.5f), 1, 1024);

            Assert.That(actual[800], Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Process_StereoInput_AveragesTheChannels()
        {
            var sut = new AudioDownsampler(48000, 16000);

            var actual = Run(sut, StereoLeftOnly(4800, 1f), 2, 1024);

            Assert.That(actual[800], Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Process_ToneBelowCutoff_KeepsItsAmplitude()
        {
            var sut = new AudioDownsampler(48000, 16000);

            var actual = Run(sut, Tone(48000, 1000, 48000), 1, 1024);

            Assert.That(Rms(actual, 1000, 15000), Is.EqualTo(0.5 / Math.Sqrt(2)).Within(2).Percent);
        }

        [Test]
        public void Process_ToneAboveOutputNyquist_IsRemoved()
        {
            var sut = new AudioDownsampler(48000, 16000);

            var actual = Run(sut, Tone(48000, 12000, 48000), 1, 1024);

            Assert.That(Rms(actual, 1000, 15000), Is.LessThan(0.005));
        }

        [TestCase(97)]
        [TestCase(1024)]
        public void Process_SplitIntoBlocks_MatchesOneBlock(int blockFrames)
        {
            var input = Tone(44100, 440, 20000);
            var expected = Run(new AudioDownsampler(44100, 16000), input, 1, input.Length);
            var sut = new AudioDownsampler(44100, 16000);

            var actual = Run(sut, input, 1, blockFrames);

            Assert.That(actual, Is.EqualTo(expected).Within(1e-6f));
        }

        [TestCase(48000)]
        [TestCase(44100)]
        public void Process_LongStream_OutputCountFollowsTheRateRatio(int inputRate)
        {
            var sut = new AudioDownsampler(inputRate, 16000);
            var expected = 10 * 16000;

            var actual = Run(sut, new float[10 * inputRate], 1, 1024).Length;

            Assert.That(actual, Is.InRange(expected - 12, expected));
        }

        [Test]
        public void Process_Impulse_KeepsItsPositionOnTheTimeline()
        {
            var sut = new AudioDownsampler(48000, 16000);
            var input = new float[9600];
            input[3000] = 1f;

            var actual = ArgMax(Run(sut, input, 1, 1024));

            Assert.That(actual, Is.EqualTo(1000));
        }

        [Test]
        public void Process_OutputSmallerThanMaxOutputCount_ThrowsArgumentException()
        {
            var sut = new AudioDownsampler(48000, 16000);

            Assert.That(() => sut.Process(new float[1024], 1, new float[10]), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Constructor_OutputRateAboveInputRate_ThrowsArgumentOutOfRangeException()
        {
            Assert.That(() => new AudioDownsampler(16000, 48000), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
