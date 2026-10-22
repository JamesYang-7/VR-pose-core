using System;

namespace VrChat.Audio2Face
{
    /// <summary>
    /// Recent Audio2Face frames on the audio timeline, sampled at an arbitrary audio position.
    ///
    /// <para>Frames arrive at the model's rate (60 fps) in bursts, while Unity renders at its own rate
    /// (90 Hz in the headset). Sampling at the audio position that is audible right now, interpolating
    /// between the two neighbouring frames, gives motion that is smooth and in step with the voice.
    /// Positions past the newest frame hold it (frames are late, e.g. when the voice is not delayed);
    /// positions before the oldest hold that one.</para>
    /// </summary>
    public sealed class FrameTimeline
    {
        readonly int _weightCount;
        readonly long[] _times;
        readonly float[] _weights;
        int _start;
        int _count;

        /// <summary>Frames currently held.</summary>
        public int Count => _count;

        /// <param name="capacity">Frames kept; the oldest is dropped when full.</param>
        public FrameTimeline(int weightCount, int capacity)
        {
            if (weightCount <= 0) throw new ArgumentOutOfRangeException(nameof(weightCount));
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _weightCount = weightCount;
            _times = new long[capacity];
            _weights = new float[capacity * weightCount];
        }

        /// <summary>Appends a frame. Timestamps must increase from frame to frame.</summary>
        public void Add(long timestamp, ReadOnlySpan<float> weights)
        {
            if (weights.Length < _weightCount) throw new ArgumentException($"weights must hold {_weightCount} values.", nameof(weights));
            if (_count > 0 && timestamp <= _times[Slot(_count - 1)])
                throw new ArgumentException("Frame timestamps must increase.", nameof(timestamp));
            if (_count == _times.Length)
            {
                _start = (_start + 1) % _times.Length;
                _count--;
            }
            var slot = Slot(_count);
            _times[slot] = timestamp;
            weights[.._weightCount].CopyTo(_weights.AsSpan(slot * _weightCount, _weightCount));
            _count++;
        }

        /// <summary>Removes every frame.</summary>
        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        /// <summary>
        /// Writes the weights at audio sample <paramref name="position"/> and forgets the frames before it
        /// that no later position can need.
        /// </summary>
        /// <returns>False, writing nothing, when no frame has been added.</returns>
        public bool Sample(long position, Span<float> output)
        {
            if (_count == 0) return false;

            // The last frame at or before position (0 when position precedes them all).
            var before = 0;
            while (before + 1 < _count && _times[Slot(before + 1)] <= position) before++;

            var a = Frame(before);
            if (before + 1 == _count || position <= _times[Slot(before)])
            {
                a.CopyTo(output);
            }
            else
            {
                var t0 = _times[Slot(before)];
                var t1 = _times[Slot(before + 1)];
                var u = (float)(position - t0) / (t1 - t0);
                var b = Frame(before + 1);
                for (var i = 0; i < _weightCount; i++) output[i] = a[i] + (b[i] - a[i]) * u;
            }

            // Positions only move forward, so frames older than `before` are done with.
            _start = Slot(before);
            _count -= before;
            return true;
        }

        int Slot(int i) => (_start + i) % _times.Length;

        ReadOnlySpan<float> Frame(int i) => _weights.AsSpan(Slot(i) * _weightCount, _weightCount);
    }
}
