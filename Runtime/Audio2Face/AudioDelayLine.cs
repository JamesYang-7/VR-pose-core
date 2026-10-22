using System;

namespace VrChat.Audio2Face
{
    /// <summary>
    /// Delays an interleaved audio stream by a fixed number of frames, in place, one block at a time.
    /// The first <see cref="DelayFrames"/> frames out are silence.
    /// </summary>
    public sealed class AudioDelayLine
    {
        readonly float[] _ring;
        int _position;

        public int DelayFrames { get; }

        public int Channels { get; }

        public AudioDelayLine(int delayFrames, int channels)
        {
            if (delayFrames < 0) throw new ArgumentOutOfRangeException(nameof(delayFrames));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            DelayFrames = delayFrames;
            Channels = channels;
            _ring = new float[delayFrames * channels];
        }

        /// <summary>Replaces <paramref name="interleaved"/> with the samples from <see cref="DelayFrames"/> frames ago.</summary>
        public void Process(Span<float> interleaved)
        {
            if (_ring.Length == 0) return;
            for (var i = 0; i < interleaved.Length; i++)
            {
                (_ring[_position], interleaved[i]) = (interleaved[i], _ring[_position]);
                if (++_position == _ring.Length) _position = 0;
            }
        }
    }
}
