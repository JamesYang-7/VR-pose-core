using NUnit.Framework;
using UnityEngine;
using VrChat.Retarget;

namespace VrChat.Testing.Retarget
{
    [TestFixture]
    public class SwingTwistTests
    {
        [Test]
        public void TwistAngle_PureTwist_ReturnsTheAngle()
        {
            var angle = SwingTwist.TwistAngle(Quaternion.AngleAxis(40f, Vector3.up), Vector3.up);

            Assert.That(angle, Is.EqualTo(40f).Within(1e-3f));
        }

        [Test]
        public void TwistAngle_PureSwing_ReturnsZero()
        {
            var angle = SwingTwist.TwistAngle(Quaternion.AngleAxis(70f, Vector3.right), Vector3.up);

            Assert.That(angle, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void TwistAngle_SwingTimesTwist_ReturnsTheTwistPart()
        {
            var q = Quaternion.AngleAxis(50f, Vector3.right) * Quaternion.AngleAxis(-35f, Vector3.up);

            var angle = SwingTwist.TwistAngle(q, Vector3.up);

            Assert.That(angle, Is.EqualTo(-35f).Within(1e-3f));
        }

        [Test]
        public void TwistAngle_OppositeAxis_FlipsTheSign()
        {
            var angle = SwingTwist.TwistAngle(Quaternion.AngleAxis(40f, Vector3.up), Vector3.down);

            Assert.That(angle, Is.EqualTo(-40f).Within(1e-3f));
        }

        [Test]
        public void TwistAngle_Beyond180_WrapsIntoHalfTurn()
        {
            var angle = SwingTwist.TwistAngle(Quaternion.AngleAxis(200f, Vector3.up), Vector3.up);

            Assert.That(angle, Is.EqualTo(-160f).Within(1e-3f));
        }
    }
}
