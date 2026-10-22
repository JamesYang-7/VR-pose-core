using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrChat.Audio2Face;

namespace VrChat.Testing.Audio2Face
{
    [TestFixture]
    public class CcMorphMixerTests
    {
        // The 52 skin and 16 tongue poses of the SDK's regression models (Claire, James, Mark), in output order.
        static readonly string[] s_audio2FacePoses =
        {
            "eyeBlinkLeft", "eyeLookDownLeft", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookUpLeft", "eyeSquintLeft", "eyeWideLeft",
            "eyeBlinkRight", "eyeLookDownRight", "eyeLookInRight", "eyeLookOutRight", "eyeLookUpRight", "eyeSquintRight", "eyeWideRight",
            "jawForward", "jawLeft", "jawRight", "jawOpen", "mouthClose", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight",
            "mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthStretchLeft", "mouthStretchRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthPressLeft", "mouthPressRight", "mouthLowerDownLeft", "mouthLowerDownRight", "mouthUpperUpLeft", "mouthUpperUpRight",
            "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight", "cheekPuff", "cheekSquintLeft",
            "cheekSquintRight", "noseSneerLeft", "noseSneerRight", "tongueOut",
            "tongueTipUp", "tongueTipDown", "tongueTipLeft", "tongueTipRight", "tongueRollUp", "tongueRollDown", "tongueRollLeft",
            "tongueRollRight", "tongueUp", "tongueDown", "tongueLeft", "tongueRight", "tongueIn", "tongueStretch", "tongueWide", "tongueNarrow",
        };

        const string AaronFbx = "Assets/Characters/Aaron-SMPL/Aaron-SMPL.fbx";

        static HashSet<string> AaronMorphs()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(AaronFbx).OfType<Mesh>().Where(m => m.name is "CC_Base_Body" or "CC_Base_Tongue"))
                for (var i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));
            return names;
        }

        static string[] DefaultMorphNames() =>
            CcMorphMixer.DefaultBlends().SelectMany(b => b.Morphs).Select(m => m.Morph).Distinct().ToArray();

        static float[] Mix(MorphMixTerm[] terms, float[] poseWeights, float intensity, int morphCount)
        {
            var morphWeights = new float[morphCount];
            CcMorphMixer.Mix(terms, poseWeights, intensity, morphWeights);
            return morphWeights;
        }

        [Test]
        public void DefaultBlends_EveryPose_IsAnAudio2FacePose()
        {
            var actual = CcMorphMixer.DefaultBlends().Select(b => b.Pose);

            Assert.That(actual, Is.SubsetOf(s_audio2FacePoses));
        }

        [Test]
        public void DefaultBlends_EyeBlinkAndLookPoses_AreLeftOut()
        {
            var actual = CcMorphMixer.DefaultBlends();

            Assert.That(actual, Has.None.Matches<A2fPoseBlend>(b => b.Pose.StartsWith("eyeBlink") || b.Pose.StartsWith("eyeLook")));
        }

        [Test]
        public void DefaultBlends_EveryMorph_ExistsOnAaron()
        {
            var aaron = AaronMorphs();
            Assume.That(aaron, Is.Not.Empty, $"{AaronFbx} is git-ignored; it comes with the local-files zip");

            var actual = DefaultMorphNames();

            Assert.That(actual, Is.SubsetOf(aaron));
        }

        [Test]
        public void Plan_KnownPoseAndMorph_ProducesATermWithTheTableWeight()
        {
            var blends = new[] { new A2fPoseBlend("jawOpen", new CcMorphWeight("Jaw_Open", 0.8f)) };

            var actual = CcMorphMixer.Plan(blends, new[] { "mouthClose", "jawOpen" }, _ => 3, null);

            Assert.That(actual, Is.EqualTo(new[] { new MorphMixTerm(1, 3, 0.8f) }));
        }

        [Test]
        public void Plan_PoseTheModelLacks_IsSkippedAndReported()
        {
            var blends = new[] { new A2fPoseBlend("tongueTwirl", new CcMorphWeight("Tongue_Out", 1f)) };
            var unresolved = new List<string>();

            CcMorphMixer.Plan(blends, new[] { "jawOpen" }, _ => 0, unresolved);

            Assert.That(unresolved, Is.EqualTo(new[] { "pose tongueTwirl" }));
        }

        [Test]
        public void Plan_MorphTheCharacterLacks_IsSkippedAndReported()
        {
            var blends = new[] { new A2fPoseBlend("jawOpen", new CcMorphWeight("Jaw_Open", 1f), new CcMorphWeight("Jaw_Hinge", 1f)) };
            var stubMorphs = new Dictionary<string, int> { ["Jaw_Open"] = 0 };
            var unresolved = new List<string>();

            var actual = CcMorphMixer.Plan(blends, new[] { "jawOpen" }, m => stubMorphs.GetValueOrDefault(m, -1), unresolved);

            Assert.That(actual.Length, Is.EqualTo(1), "terms kept");
            Assert.That(unresolved, Is.EqualTo(new[] { "morph Jaw_Hinge" }), "reported");
        }

        [Test]
        public void Mix_SinglePose_ScalesTheMorphByPoseAndTermWeight()
        {
            var terms = new[] { new MorphMixTerm(0, 0, 0.8f) };

            var actual = Mix(terms, new[] { 0.5f }, 1f, 1);

            Assert.That(actual[0], Is.EqualTo(0.4f).Within(1e-6f));
        }

        [Test]
        public void Mix_TwoPosesSharingAMorph_AddsTheirContributionsUpToOne()
        {
            var terms = new[] { new MorphMixTerm(0, 0, 1f), new MorphMixTerm(1, 0, 1f) };

            var actual = Mix(terms, new[] { 0.7f, 0.6f }, 1f, 1);

            Assert.That(actual[0], Is.EqualTo(1f));
        }

        [Test]
        public void Mix_Intensity_ScalesEveryContribution()
        {
            var terms = new[] { new MorphMixTerm(0, 0, 1f) };

            var actual = Mix(terms, new[] { 0.3f }, 1.5f, 1);

            Assert.That(actual[0], Is.EqualTo(0.45f).Within(1e-6f));
        }

        [Test]
        public void Mix_NegativePoseWeight_ClampsToZero()
        {
            var terms = new[] { new MorphMixTerm(0, 0, 1f) };

            var actual = Mix(terms, new[] { -0.2f }, 1f, 1);

            Assert.That(actual[0], Is.EqualTo(0f));
        }
    }
}
