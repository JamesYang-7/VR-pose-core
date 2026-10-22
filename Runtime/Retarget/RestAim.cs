using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// A line on each rig (from → to, e.g. shoulder → wrist) that <see cref="RetargetSolver"/> makes
    /// parallel at rest: the joint's rest is the target bind turned by the smallest swing that points
    /// the target's line along the source's. Joints sharing one line (an arm's upper arm, forearm and
    /// hand) get the same swing, so the limb is re-aimed as a whole and the angles between its bones
    /// stay copied exactly.
    /// </summary>
    public readonly struct RestAim
    {
        public Transform SourceFrom { get; }
        public Transform SourceTo { get; }
        public Transform TargetFrom { get; }
        public Transform TargetTo { get; }

        /// <summary>
        /// Swing only about the source body's up axis: the lines get the same heading, and the target's
        /// line keeps its own elevation.
        /// </summary>
        public bool HeadingOnly { get; }

        /// <summary>Whether all four points exist (an unset aim leaves the joint's bind direction alone).</summary>
        public bool IsSet => SourceFrom != null && SourceTo != null && TargetFrom != null && TargetTo != null;

        public RestAim(Transform sourceFrom, Transform sourceTo, Transform targetFrom, Transform targetTo, bool headingOnly = false)
        {
            SourceFrom = sourceFrom;
            SourceTo = sourceTo;
            TargetFrom = targetFrom;
            TargetTo = targetTo;
            HeadingOnly = headingOnly;
        }
    }
}
