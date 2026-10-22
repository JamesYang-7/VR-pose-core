using System;
using System.Collections.Generic;

namespace VrChat.Audio2Face
{
    /// <summary>
    /// Turns Audio2Face pose weights (ARKit-style skin poses plus tongue poses) into weights for a
    /// Character Creator character's morphs.
    ///
    /// <para>A CC5 export uses its own facial profile names (<c>Jaw_Open</c>, <c>Mouth_Funnel_UL</c>, …)
    /// rather than ARKit's, and several of its morphs are split into quadrants or sides where ARKit has
    /// one pose, so each pose becomes a small weighted set of morphs. Contributions are summed and
    /// clamped to [0, 1], as in <c>CcVisemeMixer</c>.</para>
    ///
    /// <para>The jaw and eyes are bones on a CC export: the Reallusion <c>BoneDriver</c> turns
    /// <c>Jaw_Open</c> / <c>Jaw_Left</c> / … on <c>CC_Base_Body</c> into jaw rotation, so the jaw is
    /// driven through those morph weights.</para>
    /// </summary>
    public static class CcMorphMixer
    {
        /// <summary>
        /// The starting ARKit → CC5 table, written from the CC5 Extended profile names on Aaron
        /// (<c>CC_Base_Body</c> and <c>CC_Base_Tongue</c>). The direct counterparts (jaw, brows, dimples,
        /// stretch, press, lip raise/depress, roll-in) should hold; the shrug and tongue-roll entries are
        /// best guesses. Expect to tune it by eye, as with <c>CcVisemeMixer.DefaultBlends</c>.
        ///
        /// <para>Left out on purpose: <c>eyeBlink*</c> and <c>eyeLook*</c> (the SDK's solver configs leave
        /// them inactive, and the eyes and lids belong to eye tracking), and <c>noseSneer*</c> (the configs
        /// solve only the left side, which would wrinkle one side of the nose).</para>
        /// </summary>
        public static A2fPoseBlend[] DefaultBlends() => new[]
        {
            P("browDownLeft", M("Brow_Down_L")),
            P("browDownRight", M("Brow_Down_R")),
            P("browInnerUp", M("Brow_Raise_In_L"), M("Brow_Raise_In_R")),
            P("browOuterUpLeft", M("Brow_Raise_Outer_L")),
            P("browOuterUpRight", M("Brow_Raise_Outer_R")),
            P("cheekPuff", M("Mouth_Cheek_Blow_L"), M("Mouth_Cheek_Blow_R")),
            P("cheekSquintLeft", M("Eye_Cheek_Raise_L")),
            P("cheekSquintRight", M("Eye_Cheek_Raise_R")),
            // Eye_Squint_L/R and Eye_Wide_L/R also exist in the FBX, but only on the lash and occlusion
            // meshes; on CC_Base_Body the CC5 profile calls them Eye_Squint_Inner_* and Eye_Widen_*.
            P("eyeSquintLeft", M("Eye_Squint_Inner_L")),
            P("eyeSquintRight", M("Eye_Squint_Inner_R")),
            P("eyeWideLeft", M("Eye_Widen_L")),
            P("eyeWideRight", M("Eye_Widen_R")),
            P("jawForward", M("Jaw_Fwd")),
            P("jawLeft", M("Jaw_Left")),
            P("jawRight", M("Jaw_Right")),
            P("jawOpen", M("Jaw_Open")),
            P("mouthClose", M("Mouth_Lips_Together_UL"), M("Mouth_Lips_Together_UR"), M("Mouth_Lips_Together_DL"), M("Mouth_Lips_Together_DR")),
            P("mouthFunnel", M("Mouth_Funnel_UL"), M("Mouth_Funnel_UR"), M("Mouth_Funnel_DL"), M("Mouth_Funnel_DR")),
            P("mouthPucker", M("Mouth_Lips_Purse_UL"), M("Mouth_Lips_Purse_UR"), M("Mouth_Lips_Purse_DL"), M("Mouth_Lips_Purse_DR")),
            P("mouthLeft", M("Mouth_Left")),
            P("mouthRight", M("Mouth_Right")),
            P("mouthSmileLeft", M("Mouth_Corner_Pull_L")),
            P("mouthSmileRight", M("Mouth_Corner_Pull_R")),
            P("mouthFrownLeft", M("Mouth_Corner_Down_L")),
            P("mouthFrownRight", M("Mouth_Corner_Down_R")),
            P("mouthDimpleLeft", M("Mouth_Dimple_L")),
            P("mouthDimpleRight", M("Mouth_Dimple_R")),
            P("mouthStretchLeft", M("Mouth_Stretch_L")),
            P("mouthStretchRight", M("Mouth_Stretch_R")),
            P("mouthRollLower", M("Mouth_LowerLip_RollIn_L"), M("Mouth_LowerLip_RollIn_R")),
            P("mouthRollUpper", M("Mouth_UpperLip_RollIn_L"), M("Mouth_UpperLip_RollIn_R")),
            P("mouthShrugLower", M("Jaw_Chin_Raise_DL"), M("Jaw_Chin_Raise_DR")),
            P("mouthShrugUpper", M("Jaw_Chin_Raise_UL", 0.5f), M("Jaw_Chin_Raise_UR", 0.5f)),
            P("mouthPressLeft", M("Mouth_Lips_Press_L")),
            P("mouthPressRight", M("Mouth_Lips_Press_R")),
            P("mouthLowerDownLeft", M("Mouth_LowerLip_Depress_L")),
            P("mouthLowerDownRight", M("Mouth_LowerLip_Depress_R")),
            P("mouthUpperUpLeft", M("Mouth_UpperLip_Raise_L")),
            P("mouthUpperUpRight", M("Mouth_UpperLip_Raise_R")),
            P("tongueOut", M("Tongue_Out")),
            P("tongueTipUp", M("Tongue_Tip_Up")),
            P("tongueTipDown", M("Tongue_Tip_Down")),
            P("tongueTipLeft", M("Tongue_Tip_Left")),
            P("tongueTipRight", M("Tongue_Tip_Right")),
            P("tongueRollUp", M("Tongue_Bend_Up")),
            P("tongueRollDown", M("Tongue_Bend_Down")),
            P("tongueRollLeft", M("Tongue_Twist_Left")),
            P("tongueRollRight", M("Tongue_Twist_Right")),
            P("tongueUp", M("Tongue_Up")),
            P("tongueDown", M("Tongue_Down")),
            P("tongueLeft", M("Tongue_Left")),
            P("tongueRight", M("Tongue_Right")),
            P("tongueIn", M("Tongue_In")),
            P("tongueStretch", M("Tongue_Thin")),
            P("tongueWide", M("Tongue_Wide")),
            P("tongueNarrow", M("Tongue_Narrow")),
        };

        /// <summary>
        /// Resolves a table against the session's pose names and the character's morphs.
        /// </summary>
        /// <param name="morphIndexOf">Index of a morph in the caller's morph list, or -1 if the character lacks it.</param>
        /// <param name="unresolved">Receives "pose X" / "morph Y" for every table entry that was skipped.</param>
        public static MorphMixTerm[] Plan(
            IReadOnlyList<A2fPoseBlend> blends, IReadOnlyList<string> poseNames, Func<string, int> morphIndexOf, ICollection<string> unresolved)
        {
            var poseIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < poseNames.Count; i++) poseIndex.TryAdd(poseNames[i], i);

            var terms = new List<MorphMixTerm>();
            foreach (var blend in blends)
            {
                if (!poseIndex.TryGetValue(blend.Pose ?? "", out var pose))
                {
                    unresolved?.Add($"pose {blend.Pose}");
                    continue;
                }
                if (blend.Morphs == null) continue;
                foreach (var m in blend.Morphs)
                {
                    var morph = string.IsNullOrEmpty(m.Morph) ? -1 : morphIndexOf(m.Morph);
                    if (morph < 0)
                    {
                        unresolved?.Add($"morph {m.Morph}");
                        continue;
                    }
                    terms.Add(new MorphMixTerm(pose, morph, m.Weight));
                }
            }
            return terms.ToArray();
        }

        /// <summary>
        /// Writes each morph's weight in [0, 1]: the sum over its terms of pose weight × term weight ×
        /// <paramref name="intensity"/>, clamped.
        /// </summary>
        public static void Mix(ReadOnlySpan<MorphMixTerm> terms, ReadOnlySpan<float> poseWeights, float intensity, Span<float> morphWeights)
        {
            morphWeights.Clear();
            foreach (var t in terms)
                morphWeights[t.Morph] += poseWeights[t.Pose] * t.Weight * intensity;
            for (var i = 0; i < morphWeights.Length; i++)
                morphWeights[i] = Math.Clamp(morphWeights[i], 0f, 1f);
        }

        static A2fPoseBlend P(string pose, params CcMorphWeight[] morphs) => new(pose, morphs);

        static CcMorphWeight M(string morph, float weight = 1f) => new(morph, weight);
    }
}
