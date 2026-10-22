using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// Fires once when a pose has been held, with the head still, for a set time: the triggers of
    /// <see cref="RigCalibrator"/>. Moving the head further than the tolerance restarts the count from
    /// the new spot; leaving the pose resets it and re-arms the trigger.
    /// </summary>
    public sealed class PoseHold
    {
        readonly float _seconds;
        readonly float _tolerance;
        float _held;
        Vector3 _start;
        bool _holding;
        bool _fired;

        /// <param name="seconds">How long the pose must be held.</param>
        /// <param name="tolerance">How far the head may move from where the hold started (m).</param>
        public PoseHold(float seconds, float tolerance)
        {
            _seconds = seconds;
            _tolerance = tolerance;
        }

        /// <summary>Share of the hold done, 0–1; stays 1 after firing until the pose is left.</summary>
        public float Progress => _fired ? 1f : _seconds > 0f ? Mathf.Clamp01(_held / _seconds) : 0f;

        /// <summary>Advances one frame. True on the frame the hold completes, once per time in the pose.</summary>
        public bool Update(bool inPose, Vector3 head, float deltaTime)
        {
            if (!inPose)
            {
                _holding = false;
                _fired = false;
                _held = 0f;
                return false;
            }
            if (_fired) return false;
            if (!_holding || (head - _start).magnitude > _tolerance)
            {
                _holding = true;
                _start = head;
                _held = 0f;
            }
            _held += deltaTime;
            if (_held < _seconds) return false;
            _fired = true;
            return true;
        }
    }
}
