using System;

namespace VrChat
{
    /// <summary>What an untracked hand's 18 input numbers become (the manifests' <c>mask_mode</c>).</summary>
    public enum HandMasking
    {
        /// <summary>The last tracked pose, still; all zero until the hand is first seen.</summary>
        Hold,
        /// <summary>RPM: all zero.</summary>
        Zero,
    }

    /// <summary>
    /// Raw device frames in, 54-float model input frames out (<c>Models/rpm/RPM-README.md</c> §4.3–4.4): each raw
    /// pose goes through <see cref="GorpDevice.ToSmpl"/>, the frame gets rotation and position velocities against
    /// the previous raw frame, and each hand below the confidence threshold is masked as <see cref="Masking"/>
    /// says. Optionally a reappearing hand gets identity / zero velocity on its first
    /// <see cref="ReappearStaticFrames"/> tracked frames (RPM: none). The head is never masked.
    ///
    /// <para>RPM's rule (<see cref="HandMasking.Zero"/>, no reappearance) reproduces the export's
    /// <c>sparse_raw</c> → <c>sparse_ctx</c>. The first raw frame after construction or <see cref="Reset"/> only
    /// seeds the velocities. Feed it at the model's frame rate.</para>
    /// </summary>
    public sealed class GorpInputProcessor
    {
        /// <summary>RPM's real-input threshold: a hand counts as tracked at or above it (GORP controllers report 0 / 1).</summary>
        public const float ConfidenceThreshold = 0.8f;

        /// <summary>A hand counts as tracked at or above this confidence; 0 never masks (RPM's synthetic-input versions).</summary>
        public float Threshold { get; }

        /// <summary>What an untracked hand becomes.</summary>
        public HandMasking Masking { get; }

        /// <summary>Tracked frames after a gap that get identity rotation velocity and zero position velocity; 0 = none.</summary>
        public int ReappearStaticFrames { get; }

        const int kRotVel = 18, kPos = 36, kPosVel = 45;
        static readonly double[] kIdentity6D = { 1, 0, 0, 0, 1, 0 };

        readonly double[][] _pos = { new double[3], new double[3], new double[3] };
        readonly double[][] _rot = { new double[9], new double[9], new double[9] };
        readonly double[][] _prevPos = { new double[3], new double[3], new double[3] };
        readonly double[][] _prevRot = { new double[9], new double[9], new double[9] };
        readonly double[][] _lastRot6 = { new double[6], new double[6] }; // last tracked hand, per hand
        readonly double[][] _lastPos = { new double[3], new double[3] };
        readonly bool[] _seen = new bool[2];
        readonly int[] _trackedRun = new int[2]; // tracked frames since the hand's last gap
        readonly double[] _frame = new double[PoseLayout.InputWidth];
        bool _hasPrev;

        public GorpInputProcessor(HandMasking masking, int reappearStaticFrames, float threshold = ConfidenceThreshold)
        {
            Threshold = threshold;
            Masking = masking;
            ReappearStaticFrames = reappearStaticFrames;
            Reset();
        }

        /// <summary>Forgets every previous frame: the next <see cref="Push"/> only seeds the velocities.</summary>
        public void Reset()
        {
            _hasPrev = false;
            _seen[0] = _seen[1] = false;
            _trackedRun[0] = _trackedRun[1] = int.MaxValue; // the first frame can never be a reappearance
        }

        /// <summary>
        /// Takes one raw frame (head, left, right; <see cref="GorpDevice.RawWidth"/> floats each) starting at
        /// <paramref name="offset"/> in <paramref name="raw"/>. Returns false on the first frame after
        /// <see cref="Reset"/>; otherwise writes the model input frame to <paramref name="frame"/> and returns true.
        /// </summary>
        public bool Push(float[] raw, int offset, float[] frame)
        {
            for (int d = 0; d < GorpDevice.Points; d++)
                GorpDevice.ToSmpl(raw, offset + d * GorpDevice.RawWidth, d, _pos[d], _rot[d]);

            bool produced = _hasPrev;
            if (produced)
            {
                for (int d = 0; d < GorpDevice.Points; d++) WritePoint(d);
                for (int h = 0; h < 2; h++) Mask(h, raw[offset + (h + 1) * GorpDevice.RawWidth + 7] >= Threshold);
                for (int k = 0; k < frame.Length; k++) frame[k] = (float)_frame[k];
            }

            for (int d = 0; d < GorpDevice.Points; d++)
            {
                Array.Copy(_pos[d], _prevPos[d], 3);
                Array.Copy(_rot[d], _prevRot[d], 9);
            }
            _hasPrev = true;
            return produced;
        }

        // Unmasked features of point d: 6D of R, 6D of R_prevᵀ·R, position, position − previous position.
        void WritePoint(int d)
        {
            var r = _rot[d];
            var p = _prevRot[d];
            for (int i = 0; i < 3; i++)
            {
                _frame[d * 6 + i] = r[i * 3];         // first column
                _frame[d * 6 + 3 + i] = r[i * 3 + 1]; // second column
            }
            for (int col = 0; col < 2; col++)
                for (int i = 0; i < 3; i++)
                    _frame[kRotVel + d * 6 + col * 3 + i] = p[i] * r[col] + p[3 + i] * r[3 + col] + p[6 + i] * r[6 + col];
            for (int i = 0; i < 3; i++)
            {
                _frame[kPos + d * 3 + i] = _pos[d][i];
                _frame[kPosVel + d * 3 + i] = _pos[d][i] - _prevPos[d][i];
            }
        }

        // README 4.4 for hand h (0 left, 1 right; point h + 1).
        void Mask(int h, bool valid)
        {
            int d = h + 1;
            if (valid)
            {
                if (_trackedRun[h] < ReappearStaticFrames) SetStill(d);
                if (_trackedRun[h] < int.MaxValue) _trackedRun[h]++;
                Array.Copy(_frame, d * 6, _lastRot6[h], 0, 6);
                Array.Copy(_frame, kPos + d * 3, _lastPos[h], 0, 3);
                _seen[h] = true;
                return;
            }
            _trackedRun[h] = 0;
            if (Masking == HandMasking.Hold && _seen[h])
            {
                Array.Copy(_lastRot6[h], 0, _frame, d * 6, 6);
                Array.Copy(_lastPos[h], 0, _frame, kPos + d * 3, 3);
                SetStill(d);
            }
            else
            {
                Array.Clear(_frame, d * 6, 6);
                Array.Clear(_frame, kRotVel + d * 6, 6);
                Array.Clear(_frame, kPos + d * 3, 3);
                Array.Clear(_frame, kPosVel + d * 3, 3);
            }
        }

        void SetStill(int d)
        {
            Array.Copy(kIdentity6D, 0, _frame, kRotVel + d * 6, 6);
            Array.Clear(_frame, kPosVel + d * 3, 3);
        }
    }
}
