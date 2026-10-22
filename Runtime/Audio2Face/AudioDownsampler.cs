using System;

namespace VrChat.Audio2Face
{
    /// <summary>
    /// Streams interleaved audio at the output-device rate (48 kHz typically) into mono at a lower rate
    /// (16 kHz for Audio2Face), one <c>OnAudioFilterRead</c> block at a time.
    ///
    /// <para>Channels are averaged, then a windowed-sinc low-pass (cut at 0.4 × the output rate) is
    /// evaluated at each output instant, interpolating linearly between input samples when the ratio is
    /// not an integer. Output sample k sits exactly at input time k · inputRate / outputRate (integer
    /// arithmetic, so a long stream does not drift), and the filter is centred on it, so the output adds
    /// no delay: the 16 kHz timeline lines up with the audio the device plays. The price is that the last
    /// few output samples of a block wait for the next block's input.</para>
    /// </summary>
    public sealed class AudioDownsampler
    {
        readonly int _inputRate;
        readonly int _outputRate;
        readonly float[] _taps;
        readonly int _half;

        float[] _buffer;
        int _count;
        long _bufferStart; // absolute input index of _buffer[0]
        long _nextOutput;  // absolute index of the next output sample

        public int InputRate => _inputRate;

        public int OutputRate => _outputRate;

        /// <param name="halfTaps">Filter half-length in input samples; 32 gives 65 taps.</param>
        public AudioDownsampler(int inputRate, int outputRate, int halfTaps = 32)
        {
            if (outputRate <= 0 || inputRate < outputRate)
                throw new ArgumentOutOfRangeException(nameof(outputRate), $"Need 0 < outputRate <= inputRate, got {outputRate} and {inputRate}.");
            _inputRate = inputRate;
            _outputRate = outputRate;
            _half = halfTaps;
            _taps = DesignLowPass(0.4 * outputRate / inputRate, halfTaps);
            _buffer = new float[4096];
            // Start with a half-window of silence so the first output has a full window behind it.
            _count = _half;
            _bufferStart = -_half;
        }

        /// <summary>The most samples <see cref="Process"/> can write for <paramref name="inputFrames"/> input frames.</summary>
        public int MaxOutputCount(int inputFrames) => (int)((long)inputFrames * _outputRate / _inputRate) + 2;

        /// <summary>
        /// Consumes one block of interleaved samples and writes the mono output samples that are now complete.
        /// </summary>
        /// <param name="output">At least <see cref="MaxOutputCount"/> long.</param>
        /// <returns>The number of samples written to <paramref name="output"/>.</returns>
        public int Process(ReadOnlySpan<float> interleaved, int channels, Span<float> output)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            var frames = interleaved.Length / channels;
            if (output.Length < MaxOutputCount(frames))
                throw new ArgumentException($"output must hold at least {MaxOutputCount(frames)} samples.", nameof(output));

            EnsureCapacity(_count + frames);
            var scale = 1f / channels;
            for (var f = 0; f < frames; f++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++) sum += interleaved[f * channels + c];
                _buffer[_count + f] = sum * scale;
            }
            _count += frames;

            var written = 0;
            while (true)
            {
                var position = _nextOutput * _inputRate;
                var index = position / _outputRate;
                var remainder = position - index * _outputRate;
                // The window around index + 1 must be complete (index + 1 is only read when interpolating,
                // but requiring it keeps the integer-ratio and fractional cases on the same schedule).
                if (index + 1 + _half >= _bufferStart + _count) break;
                var a = Filter(index);
                output[written++] = remainder == 0 ? a : a + (Filter(index + 1) - a) * remainder / _outputRate;
                _nextOutput++;
            }

            var keepFrom = _nextOutput * _inputRate / _outputRate - _half;
            var drop = (int)(keepFrom - _bufferStart);
            if (drop > 0)
            {
                Array.Copy(_buffer, drop, _buffer, 0, _count - drop);
                _count -= drop;
                _bufferStart += drop;
            }
            return written;
        }

        float Filter(long index)
        {
            var centre = (int)(index - _bufferStart);
            var sum = 0f;
            for (var k = 0; k < _taps.Length; k++) sum += _taps[k] * _buffer[centre - _half + k];
            return sum;
        }

        void EnsureCapacity(int size)
        {
            if (size > _buffer.Length) Array.Resize(ref _buffer, Math.Max(size, _buffer.Length * 2));
        }

        // Blackman-windowed sinc, unity gain at DC. cutoff is a fraction of the input rate.
        static float[] DesignLowPass(double cutoff, int half)
        {
            var taps = new float[2 * half + 1];
            var sum = 0.0;
            for (var n = -half; n <= half; n++)
            {
                var x = 2.0 * cutoff * n;
                var sinc = n == 0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                var w = 0.42 + 0.5 * Math.Cos(Math.PI * n / half) + 0.08 * Math.Cos(2.0 * Math.PI * n / half);
                var v = 2.0 * cutoff * sinc * w;
                taps[n + half] = (float)v;
                sum += v;
            }
            for (var i = 0; i < taps.Length; i++) taps[i] = (float)(taps[i] / sum);
            return taps;
        }
    }
}
