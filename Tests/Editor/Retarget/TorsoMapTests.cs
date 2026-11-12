using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;
using VrChat.Retarget;

namespace VrChat.Testing.Retarget
{
    [TestFixture]
    public class TorsoMapTests
    {
        static readonly Vector3EqualityComparer s_micrometre = new(1e-6f);

        // A source torso 1 m from the hips up to the eyes, and a target one 0.6 m long, standing 1 m to its side.
        static readonly Vector3 s_sourceHips = new(0f, 1f, 0f), s_sourceEyes = new(0f, 2f, 0f);
        static readonly Vector3 s_targetHips = new(1f, 1f, 0f), s_targetEyes = new(1f, 1.6f, 0f);

        static Vector3 Map(Vector3 point) => TorsoMap.Point(point, s_sourceHips, s_sourceEyes, s_targetHips, s_targetEyes);

        [Test]
        public void Point_AtTheSourceHipHeight_KeepsItsOffsetFromTheHips()
        {
            var actual = Map(new Vector3(0.2f, 1f, 0.3f));

            Assert.That(actual, Is.EqualTo(new Vector3(1.2f, 1f, 0.3f)).Using(s_micrometre));
        }

        [Test]
        public void Point_AtTheSourceEyeHeight_KeepsItsOffsetFromTheEyes()
        {
            var actual = Map(new Vector3(0.2f, 2f, 0.3f));

            Assert.That(actual, Is.EqualTo(new Vector3(1.2f, 1.6f, 0.3f)).Using(s_micrometre));
        }

        [Test]
        public void Point_HalfwayUpTheSourceTorso_BlendsBothOffsetsEqually()
        {
            var actual = Map(new Vector3(0.2f, 1.5f, 0.3f));

            Assert.That(actual, Is.EqualTo(new Vector3(1.2f, 1.3f, 0.3f)).Using(s_micrometre));
        }

        [Test]
        public void Point_AQuarterUpTheSourceTorso_TakesTheSmoothstepShareOfTheEyeOffset()
        {
            var actual = Map(new Vector3(0.2f, 1.25f, 0.3f));

            // smoothstep(0.25) = 0.15625 of the torsos' 0.4 m difference.
            Assert.That(actual, Is.EqualTo(new Vector3(1.2f, 1.25f - 0.15625f * 0.4f, 0.3f)).Using(s_micrometre));
        }

        [Test]
        public void Point_BelowTheSourceHips_KeepsItsOffsetFromTheHips()
        {
            var actual = Map(new Vector3(0.2f, 0.7f, 0.3f));

            Assert.That(actual, Is.EqualTo(new Vector3(1.2f, 0.7f, 0.3f)).Using(s_micrometre));
        }

        [Test]
        public void Point_AboveTheSourceEyes_KeepsItsOffsetFromTheEyes()
        {
            var actual = Map(new Vector3(0.2f, 2.3f, 0.3f));

            Assert.That(actual, Is.EqualTo(new Vector3(1.2f, 1.9f, 0.3f)).Using(s_micrometre));
        }

        [Test]
        public void Point_SourceEyesOnItsHips_KeepsItsOffsetFromTheHips()
        {
            var actual = TorsoMap.Point(new Vector3(0.2f, 1.5f, 0.3f), s_sourceHips, s_sourceHips, s_targetHips, s_targetEyes);

            Assert.That(actual, Is.EqualTo(new Vector3(1.2f, 1.5f, 0.3f)).Using(s_micrometre));
        }
    }
}
