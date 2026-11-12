using System;
using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// A pose model's results on the model's own clock, shown at a steady delay. Each result belongs to the tick its
    /// input frame was sampled at, one model frame (<c>tick</c>) after the last. <see cref="Sample"/> shows the pose
    /// at <see cref="Delay"/> before now, interpolated between the two results that bracket that time. So the body
    /// moves at a steady speed between results, whenever each result happens to arrive.
    ///
    /// <para>Why not blend from the shown pose to the newest result over one model frame from its arrival, as
    /// <c>PoseLiveWorker</c> did until 2026-11-12: at a 72 Hz display, the model's 30 Hz results arrive 2 or 3 frames
    /// apart (27.8 or 41.7 ms). After a 3-frame gap the blend had finished 8 ms early, so the body nearly stopped for a
    /// frame, then moved on. On a seated body, still at the hips, that showed as the spine jerking a little, several
    /// times a second. On a 127 s Quest session it slowed 40% of the model frames; the model's own results are smooth.</para>
    ///
    /// <para>The delay is one model frame plus the latency, the time from a tick to its result, plus a small margin.
    /// Even at a steady display rate the latency varies by a frame, with where each tick falls within its frame. The
    /// delay allows for the 90th percentile of the last 2 s of results. A result later than that (a slow readback)
    /// holds the newest pose for a frame rather than guess past it. Why not the recent peak: one slow readback then
    /// added its latency to every frame for seconds (a 75 ms delay at the median, 93 ms at the 95th percentile, on a
    /// replayed Quest session, against about 61 ms now). The display clock follows the delay smoothly: it closes its
    /// gap over about <see cref="ClockSettle"/>, never running more than <see cref="ClockSlew"/> fast or slow, so it
    /// never steps. Why not follow at the full slew: the delay's changes then show as speed changes of that size.</para>
    /// </summary>
    public sealed class PoseTimeline
    {
        /// <summary>The most the display clock runs fast or slow to follow a change in the delay (a share of real time).</summary>
        public const float ClockSlew = 0.1f;

        /// <summary>The time over which the display clock closes its gap to the delay (s).</summary>
        public const float ClockSettle = 0.5f;

        // The latency allowed for: this share of the last this many results (2 s at 30 Hz) came at least as soon.
        const int LatencyWindow = 60;
        const float LatencyShare = 0.9f;
        // Added to the delay, for the clock's lag behind a rising delay (s).
        const float Margin = 0.002f;
        // The display clock jumps instead of slewing when it is this many model frames off (the first result, a hitch).
        const float SnapFrames = 2f;
        // Results held: the newest, the pair being shown and one to spare.
        const int Capacity = 4;

        readonly float _tick;
        readonly Quaternion[][] _rotations; // index 0 is the newest
        readonly Vector3[] _pelvis = new Vector3[Capacity];
        readonly float[] _times = new float[Capacity];
        int _count;
        readonly float[] _latencies = new float[LatencyWindow]; // a ring of the last results' latencies
        readonly float[] _sorted = new float[LatencyWindow];
        int _latencyCount, _latencyNext;
        float _latency;
        float _clock;
        float _lastNow;
        bool _clockSet;

        /// <param name="joints">Rotations per pose.</param>
        /// <param name="tick">The model frame: the time between two ticks (s).</param>
        public PoseTimeline(int joints, float tick)
        {
            if (tick <= 0f) throw new ArgumentOutOfRangeException(nameof(tick), "The model frame must be positive.");
            _tick = tick;
            _rotations = new Quaternion[Capacity][];
            for (var i = 0; i < Capacity; i++) _rotations[i] = new Quaternion[joints];
        }

        /// <summary>Whether a result has been added.</summary>
        public bool HasPose => _count > 0;

        /// <summary>The time from a tick to its result allowed for (s): the 90th percentile of the last 2 s of results.</summary>
        public float Latency => _latency;

        /// <summary>How far behind now the pose is meant to show (s): one model frame plus <see cref="Latency"/> and a 2 ms margin.</summary>
        public float Delay => _tick + _latency + Margin;

        /// <summary>
        /// Adds the result of the tick at <paramref name="tickTime"/>, arrived at <paramref name="now"/> (the same clock,
        /// in seconds). Results come in tick order. The rotations and pelvis are copied.
        /// </summary>
        public void Add(float tickTime, float now, Quaternion[] rotations, Vector3 pelvis)
        {
            var reused = _rotations[Capacity - 1];
            for (var i = Capacity - 1; i > 0; i--)
            {
                _rotations[i] = _rotations[i - 1];
                _pelvis[i] = _pelvis[i - 1];
                _times[i] = _times[i - 1];
            }
            Array.Copy(rotations, reused, reused.Length);
            _rotations[0] = reused;
            _pelvis[0] = pelvis;
            _times[0] = tickTime;
            _count = Math.Min(_count + 1, Capacity);

            _latencies[_latencyNext] = now - tickTime;
            _latencyNext = (_latencyNext + 1) % LatencyWindow;
            _latencyCount = Math.Min(_latencyCount + 1, LatencyWindow);
            Array.Copy(_latencies, _sorted, _latencyCount);
            Array.Sort(_sorted, 0, _latencyCount);
            _latency = _sorted[Math.Min(_latencyCount - 1, (int)(LatencyShare * _latencyCount))];
        }

        /// <summary>
        /// Writes the pose at <see cref="Delay"/> before <paramref name="now"/> into <paramref name="rotations"/> and
        /// <paramref name="pelvis"/>: between the two results around that time, or the newest if none is that recent
        /// yet. Call once per rendered frame. False, writing nothing, before the first result.
        /// </summary>
        public bool Sample(float now, Quaternion[] rotations, out Vector3 pelvis)
        {
            pelvis = default;
            if (_count == 0) return false;
            var dt = _clockSet ? Mathf.Max(0f, now - _lastNow) : 0f;
            _lastNow = now;

            var target = now - Delay;
            if (!_clockSet || Mathf.Abs(target - _clock) > SnapFrames * _tick)
            {
                _clock = target;
                _clockSet = true;
            }
            else
            {
                var gap = target - (_clock + dt);
                _clock += dt + Mathf.Clamp(gap * dt / ClockSettle, -ClockSlew * dt, ClockSlew * dt);
            }

            // Never past the newest result, never before the oldest held.
            var at = Mathf.Clamp(_clock, _times[_count - 1], _times[0]);
            var newer = 0;
            while (newer < _count - 1 && _times[newer + 1] > at) newer++;
            if (newer == _count - 1)
            {
                Newest(newer, rotations, out pelvis);
                return true;
            }
            var older = newer + 1;
            var span = _times[newer] - _times[older];
            var a = span > 0f ? (at - _times[older]) / span : 1f;
            for (var j = 0; j < rotations.Length; j++) rotations[j] = Quaternion.Slerp(_rotations[older][j], _rotations[newer][j], a);
            pelvis = Vector3.Lerp(_pelvis[older], _pelvis[newer], a);
            return true;
        }

        /// <summary>Writes the newest result, as it arrived. False, writing nothing, before the first result.</summary>
        public bool Newest(Quaternion[] rotations, out Vector3 pelvis)
        {
            pelvis = default;
            if (_count == 0) return false;
            Newest(0, rotations, out pelvis);
            return true;
        }

        void Newest(int index, Quaternion[] rotations, out Vector3 pelvis)
        {
            Array.Copy(_rotations[index], rotations, rotations.Length);
            pelvis = _pelvis[index];
        }
    }
}
