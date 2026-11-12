namespace VrChat.Retarget
{
    /// <summary>Where <see cref="SmplRetarget.PinWrists"/> puts the target's wrists.</summary>
    public enum WristGoal
    {
        /// <summary>
        /// On the source's wrists. For the wearer's own body, whose source wrists are on the controllers.
        /// </summary>
        Source,

        /// <summary>
        /// On the source's wrists moved onto the target's body by <see cref="TorsoMap.Point"/>: a hand at the source's
        /// lap goes to the target's lap, and one at the source's face to the target's face. For a body of other
        /// proportions than the source's, seen from outside: copying the joint angles alone puts a shorter torso's
        /// hands into its thighs when the source's rest on its lap.
        /// </summary>
        BodyScaled,
    }
}
