using System;

namespace VrChat
{
    /// <summary>What keeps a headset and two controllers from reading as a T-pose (<see cref="RigCalibration.CheckTPose"/>).</summary>
    [Flags]
    public enum TPoseMisses
    {
        None = 0,
        HeadTilted = 1,
        ArmsNotLevel = 2,
        HandsNotAtShoulders = 4,
        HandsTooClose = 8,
        HeadNotBetweenHands = 16,
    }
}
