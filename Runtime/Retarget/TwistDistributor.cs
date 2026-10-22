using System.Collections.Generic;
using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// Shares a limb segment's roll out along its twist bones, so the skin rolls gradually
    /// instead of pinching at the joints ("candy wrapper").
    ///
    /// <para>The segment (e.g. an upper arm) carries its own twist θs relative to its rest, and
    /// the distal bone (e.g. the forearm) carries θd about the same axis. A twist bone a fraction
    /// <c>t</c> along the segment (0 at the segment's joint, 1 at the distal joint) should roll
    /// by <c>θs·t + θd·t</c> in total; since it already inherits θs from the segment it gets
    /// <c>−(1−t)·θs + t·θd</c> on top. So a twist bone at the shoulder cancels the arm's roll,
    /// one at mid-forearm takes half the wrist's roll. Written as world rotations, so twist
    /// bones nested under each other (CC5's Twist02 under Twist01) come out right.</para>
    /// </summary>
    public sealed class TwistDistributor
    {
        readonly struct TwistBone
        {
            public readonly Transform Bone;
            public readonly Quaternion RestInSegment;
            public readonly Vector3 AxisInBone;
            public readonly float T;

            public TwistBone(Transform bone, Quaternion restInSegment, Vector3 axisInBone, float t)
            {
                Bone = bone;
                RestInSegment = restInSegment;
                AxisInBone = axisInBone;
                T = t;
            }
        }

        readonly Transform _segment;
        readonly Transform _distal;
        readonly Quaternion _invSegmentRest;
        readonly Quaternion _invDistalRest;
        readonly Vector3 _axisInSegment;
        readonly Vector3 _axisInDistal;
        readonly TwistBone[] _bones;

        public int BoneCount => _bones.Length;

        /// <summary>
        /// Must be built while the rig is in its bind pose: the bind local rotations of the segment and
        /// distal bone count as "no twist", and each twist bone's placement is read from it.
        /// </summary>
        /// <param name="segment">The limb bone the twist bones hang off.</param>
        /// <param name="distal">The next joint down the limb (a child of <paramref name="segment"/>).</param>
        /// <param name="twistBones">Descendants of the segment, parents before children.</param>
        public TwistDistributor(Transform segment, Transform distal, IReadOnlyList<Transform> twistBones)
        {
            _segment = segment;
            _distal = distal;
            _invSegmentRest = Quaternion.Inverse(segment.localRotation);
            _invDistalRest = Quaternion.Inverse(distal.localRotation);
            _axisInSegment = distal.localPosition.normalized;
            _axisInDistal = _invDistalRest * _axisInSegment;

            float length = distal.localPosition.magnitude;
            var invSegment = Quaternion.Inverse(segment.rotation);
            _bones = new TwistBone[twistBones.Count];
            for (int i = 0; i < _bones.Length; i++)
            {
                var b = twistBones[i];
                var restInSegment = invSegment * b.rotation;
                var along = Vector3.Dot(segment.InverseTransformPoint(b.position), _axisInSegment);
                float t = length > 1e-6f ? Mathf.Clamp01(along / length) : 0f;
                _bones[i] = new TwistBone(b, restInSegment, Quaternion.Inverse(restInSegment) * _axisInSegment, t);
            }
        }

        public void Apply()
        {
            float segmentTwist = SwingTwist.TwistAngle(_invSegmentRest * _segment.localRotation, _axisInSegment);
            float distalTwist = SwingTwist.TwistAngle(_invDistalRest * _distal.localRotation, _axisInDistal);
            var segmentRotation = _segment.rotation;
            foreach (var b in _bones)
            {
                float angle = -(1f - b.T) * segmentTwist + b.T * distalTwist;
                b.Bone.rotation = segmentRotation * b.RestInSegment * Quaternion.AngleAxis(angle, b.AxisInBone);
            }
        }
    }
}
