using System.Collections.Generic;
using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// Angle-matching retargeting: each target joint turns by exactly the rotation its source joint
    /// turns, expressed in the body's canonical frame. Positions are ignored.
    ///
    /// <para>Calibration (<see cref="Calibrate"/>) takes the source's rest pose and the target's bind
    /// pose to be <i>the same pose</i> (both the canonical T-pose) and records, per joint,
    /// <c>offset = inverse(sourceRest) * toSource * targetBind</c>, where <c>toSource</c> turns the
    /// target's canonical frame onto the source's. Per frame (<see cref="Apply()"/>):
    /// <c>target.rotation = source.rotation * offset</c>. If the source joint has turned by a world
    /// delta Δ since rest, the target gets the same Δ on its bind, so every joint bend, measured in
    /// body axes, is identical on both rigs whatever their bone-axis conventions.</para>
    ///
    /// <para>Joints with a <see cref="RetargetJoint.Aim"/> instead take, as their rest, the bind turned
    /// by the smallest swing that points the target's aim line along the source's at rest:
    /// <c>offset = inverse(sourceRest) * swing * toSource * targetBind</c>. Then the line points where
    /// the source's points in every pose, not just turns by the same Δ (for a heading-only aim, it has
    /// the same heading). <see cref="SmplRetarget"/> uses this for the arms and clavicles.</para>
    ///
    /// <para>Why not aim every joint: rest directions of the trunk reflect where each template puts
    /// its joints (SMPL's collar→shoulder rises ~23°, its short spine segments zig-zag up to 25°), not a
    /// posture. Aiming those bent a CC5 target into a permanent shrug and an S-shaped spine.</para>
    /// </summary>
    public sealed class RetargetSolver
    {
        readonly RetargetJoint[] _joints;
        readonly Quaternion[] _offsets;

        public int JointCount => _joints.Length;

        public RetargetSolver(IReadOnlyList<RetargetJoint> joints)
        {
            _joints = new RetargetJoint[joints.Count];
            for (int i = 0; i < _joints.Length; i++) _joints[i] = joints[i];
            _offsets = new Quaternion[_joints.Length];
        }

        /// <summary>
        /// Records the per-joint offsets. The source must be in its rest pose and the target in its
        /// bind pose; neither is modified.
        /// </summary>
        /// <param name="sourceFacing">The source body's canonical frame at rest (see <see cref="SkeletonFacing"/>).</param>
        /// <param name="targetFacing">The target body's canonical frame at bind.</param>
        public void Calibrate(Quaternion sourceFacing, Quaternion targetFacing)
        {
            var toSource = sourceFacing * Quaternion.Inverse(targetFacing);
            for (int i = 0; i < _joints.Length; i++)
            {
                var j = _joints[i];
                var rest = toSource * j.Target.rotation;
                var aim = j.Aim;
                if (aim.IsSet)
                {
                    var sourceLine = aim.SourceTo.position - aim.SourceFrom.position;
                    var targetLine = toSource * (aim.TargetTo.position - aim.TargetFrom.position);
                    if (aim.HeadingOnly)
                    {
                        var up = sourceFacing * Vector3.up;
                        sourceLine = Vector3.ProjectOnPlane(sourceLine, up);
                        targetLine = Vector3.ProjectOnPlane(targetLine, up);
                    }
                    rest = Quaternion.FromToRotation(targetLine, sourceLine) * rest;
                }
                _offsets[i] = Quaternion.Inverse(j.Source.rotation) * rest;
            }
        }

        /// <summary>Writes every target joint's world rotation from its source joint.</summary>
        public void Apply()
        {
            for (int i = 0; i < _joints.Length; i++) Apply(i);
        }

        /// <summary>Writes one target joint's world rotation (e.g. to restore a hand after arm IK).</summary>
        public void Apply(int index)
        {
            var j = _joints[index];
            j.Target.rotation = j.Source.rotation * _offsets[index];
        }
    }
}
