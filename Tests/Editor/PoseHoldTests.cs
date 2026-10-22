using NUnit.Framework;
using UnityEngine;

namespace VrChat.Testing
{
    [TestFixture]
    public class PoseHoldTests
    {
        const float Frame = 0.1f;
        static readonly Vector3 kHead = new(0f, 1.6f, 0f);

        // Feeds whole frames; returns how many of them fired.
        static int Run(PoseHold sut, bool inPose, Vector3 head, int frames)
        {
            int fired = 0;
            for (int i = 0; i < frames; i++)
                if (sut.Update(inPose, head, Frame)) fired++;
            return fired;
        }

        [Test]
        public void Update_PoseHeldLongEnough_FiresOnce()
        {
            var sut = new PoseHold(seconds: 1f, tolerance: 0.05f);

            var fired = Run(sut, inPose: true, kHead, frames: 30);

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void Update_PoseLeftBeforeTheTime_DoesNotFireAndResets()
        {
            var sut = new PoseHold(seconds: 1f, tolerance: 0.05f);
            Run(sut, inPose: true, kHead, frames: 8);

            var fired = Run(sut, inPose: false, kHead, frames: 1);

            Assert.That(fired, Is.Zero, "fired");
            Assert.That(sut.Progress, Is.Zero, "progress");
        }

        [Test]
        public void Update_PoseLeftAndTakenAgain_FiresAgain()
        {
            var sut = new PoseHold(seconds: 1f, tolerance: 0.05f);
            Run(sut, inPose: true, kHead, frames: 15);
            Run(sut, inPose: false, kHead, frames: 1);

            var fired = Run(sut, inPose: true, kHead, frames: 15);

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void Update_HeadMovesPastTheTolerance_RestartsTheCount()
        {
            var sut = new PoseHold(seconds: 1f, tolerance: 0.05f);
            Run(sut, inPose: true, kHead, frames: 8);

            var fired = Run(sut, inPose: true, kHead + new Vector3(0.1f, 0f, 0f), frames: 8);

            Assert.That(fired, Is.Zero, "fired");
            Assert.That(sut.Progress, Is.EqualTo(0.8f).Within(1e-4f), "progress since the move");
        }
    }
}
