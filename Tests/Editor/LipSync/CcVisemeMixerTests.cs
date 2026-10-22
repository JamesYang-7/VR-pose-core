using System;
using NUnit.Framework;
using VrChat.LipSync;

namespace VrChat.Testing.LipSync
{
    [TestFixture]
    public class CcVisemeMixerTests
    {
        static float[] Visemes(params (string name, float score)[] scores)
        {
            var visemes = new float[CcVisemeMixer.OculusVisemeCount];
            foreach (var (name, score) in scores)
                visemes[Array.IndexOf(CcVisemeMixer.OculusVisemeNames, name)] = score;
            return visemes;
        }

        static float[] Mix(OculusVisemeBlend[] blends, float[] visemes)
        {
            var weights = new float[CcVisemeMixer.CcVisemeCount];
            CcVisemeMixer.Mix(blends, visemes, weights);
            return weights;
        }

        [Test]
        public void DefaultBlends_HasOneEntryPerOculusVisemeInOculusOrder()
        {
            var actual = Array.ConvertAll(CcVisemeMixer.DefaultBlends(), b => b.Viseme);

            Assert.That(actual, Is.EqualTo(CcVisemeMixer.OculusVisemeNames));
        }

        [Test]
        public void BlendShapeName_EveryCcViseme_IsACcBlendShapeName()
        {
            foreach (CcViseme v in Enum.GetValues(typeof(CcViseme)))
                Assert.That(CcVisemeMixer.BlendShapeName(v), Does.StartWith("V_"), v.ToString());
        }

        [Test]
        public void Mix_Silence_ReturnsAllZero()
        {
            var actual = Mix(CcVisemeMixer.DefaultBlends(), Visemes(("sil", 1f)));

            Assert.That(actual, Is.All.EqualTo(0f));
        }

        [Test]
        public void Mix_SingleViseme_ScalesItsShapesByTheScore()
        {
            var blends = new[] { new OculusVisemeBlend("sil"), new OculusVisemeBlend("PP", new CcShapeWeight(CcViseme.Explosive, 0.8f)) };

            var actual = Mix(blends, Visemes(("PP", 0.5f)));

            Assert.That(actual[(int)CcViseme.Explosive], Is.EqualTo(0.4f).Within(1e-6f));
        }

        [Test]
        public void Mix_TwoVisemesSharingAShape_AddsTheirContributions()
        {
            var blends = new[]
            {
                new OculusVisemeBlend("sil"),
                new OculusVisemeBlend("PP", new CcShapeWeight(CcViseme.Open, 0.3f)),
                new OculusVisemeBlend("FF", new CcShapeWeight(CcViseme.Open, 0.2f)),
            };

            var actual = Mix(blends, Visemes(("PP", 1f), ("FF", 1f)));

            Assert.That(actual[(int)CcViseme.Open], Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void Mix_SumAboveOne_ClampsToOne()
        {
            var blends = new[]
            {
                new OculusVisemeBlend("sil"),
                new OculusVisemeBlend("PP", new CcShapeWeight(CcViseme.Open, 0.9f)),
                new OculusVisemeBlend("FF", new CcShapeWeight(CcViseme.Open, 0.9f)),
            };

            var actual = Mix(blends, Visemes(("PP", 1f), ("FF", 1f)));

            Assert.That(actual[(int)CcViseme.Open], Is.EqualTo(1f));
        }

        [Test]
        public void Mix_PreviousFrameWeights_AreCleared()
        {
            var blends = CcVisemeMixer.DefaultBlends();
            var weights = new float[CcVisemeMixer.CcVisemeCount];
            CcVisemeMixer.Mix(blends, Visemes(("aa", 1f)), weights);

            CcVisemeMixer.Mix(blends, Visemes(("sil", 1f)), weights);

            Assert.That(weights, Is.All.EqualTo(0f));
        }
    }
}
