using NUnit.Framework;
using UnityEngine;
using VrChat.Retarget;

namespace VrChat.Testing.Retarget
{
    [TestFixture]
    public class SkeletonFacingTests
    {
        [Test]
        public void FromJoints_HipsAcrossPlusX_FacesPlusZ()
        {
            var facing = SkeletonFacing.FromJoints(Vector3.up, new Vector3(-0.1f, 0.9f, 0f), new Vector3(0.1f, 0.9f, 0f));

            Assert.That(Quaternion.Angle(facing, Quaternion.identity), Is.LessThan(1e-3f));
        }

        [Test]
        public void FromJoints_HipsSwapped_ReturnsHalfTurnYaw()
        {
            var facing = SkeletonFacing.FromJoints(Vector3.up, new Vector3(0.1f, 0.9f, 0f), new Vector3(-0.1f, 0.9f, 0f));

            Assert.That(Quaternion.Angle(facing, Quaternion.Euler(0f, 180f, 0f)), Is.LessThan(1e-3f));
        }

        [Test]
        public void FromJoints_HipsNotLevel_KeepsTheGivenUp()
        {
            var facing = SkeletonFacing.FromJoints(Vector3.up, new Vector3(-0.1f, 0.85f, 0.02f), new Vector3(0.1f, 0.95f, -0.02f));

            Assert.That(Vector3.Angle(facing * Vector3.up, Vector3.up), Is.LessThan(1e-3f));
        }
    }
}
