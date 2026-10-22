using System;

namespace VrChat.Retarget
{
    /// <summary>Which parts <see cref="SmplRetarget"/> re-aims along SMPL's rest directions (see <see cref="RestAim"/>).</summary>
    [Flags]
    public enum LimbAims
    {
        None = 0,

        /// <summary>Each arm along SMPL's shoulder → wrist line: upper arm, forearm and hand share one swing.</summary>
        Arms = 1,

        /// <summary>
        /// Each clavicle turned about the body's vertical so its collar → shoulder line has SMPL's heading,
        /// keeping its own elevation: moves the shoulder joint forward or back, never up.
        /// </summary>
        Clavicles = 2,
    }
}
