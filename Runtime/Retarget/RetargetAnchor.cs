namespace VrChat.Retarget
{
    /// <summary>
    /// Where <see cref="SmplRetarget"/> places the target on the source each frame: it moves the target's hips so this
    /// point lands on the source's, a pure translation that leaves the joint angles alone.
    /// </summary>
    public enum RetargetAnchor
    {
        /// <summary>Not moved: the target stays where its own transforms put it.</summary>
        None,

        /// <summary>
        /// Eye midpoint on eye midpoint (head joint on head joint if a rig has no eye bones). For the wearer's own body:
        /// the headset sits on the wearer's eyes, so the body is seen from its eyes. A body of other proportions than
        /// the source's then ends higher or lower at the hips and feet.
        /// </summary>
        Eyes,

        /// <summary>
        /// Hip-joint midpoint on hip-joint midpoint. For a body seen from outside, such as a bot: it keeps the source's
        /// seat, and its feet when its legs are as long as the source's, while its eyes sit where its own torso puts
        /// them. Hip joints, not the pelvis bones: each template puts its pelvis joint somewhere else (SMPL-X's 9 cm
        /// above its hip joints, CC5's 1–2 cm), while the hip joints are the same anatomy on both.
        /// </summary>
        Hips,
    }
}
