using System;
using System.Collections.Generic;

namespace VrChat.LipSync
{
    /// <summary>
    /// Turns the 15 Oculus LipSync viseme scores into weights for the 15 Character Creator visemes.
    ///
    /// <para>Oculus has 15 visemes, CC has 8 lip shapes plus 7 tongue shapes, and they don't line up
    /// one to one, so each Oculus viseme is a small blend of CC shapes. The contributions are summed
    /// and clamped to [0, 1]. <c>OVRLipSyncContextMorphTarget</c> can't do this: it maps each viseme
    /// to a single blendshape and overwrites rather than adds when two visemes share one.</para>
    ///
    /// <para>The jaw is not a weight here. On a CC export the Reallusion <c>BoneDriver</c> turns
    /// <c>V_Open</c> into the jaw-bone rotation, so a blend opens the jaw through its
    /// <see cref="CcViseme.Open"/> share.</para>
    /// </summary>
    public static class CcVisemeMixer
    {
        /// <summary>Oculus viseme order, as in <c>OVRLipSync.Viseme</c>.</summary>
        public static readonly string[] OculusVisemeNames =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou",
        };

        public static int OculusVisemeCount => OculusVisemeNames.Length;

        public static int CcVisemeCount => Enum.GetValues(typeof(CcViseme)).Length;

        /// <summary>The blendshape name of a CC viseme on a CC3+/CC5 export.</summary>
        public static string BlendShapeName(CcViseme viseme) => viseme switch
        {
            CcViseme.Open => "V_Open",
            CcViseme.Explosive => "V_Explosive",
            CcViseme.DentalLip => "V_Dental_Lip",
            CcViseme.TightO => "V_Tight_O",
            CcViseme.Tight => "V_Tight",
            CcViseme.Wide => "V_Wide",
            CcViseme.Affricate => "V_Affricate",
            CcViseme.LipOpen => "V_Lip_Open",
            CcViseme.TongueUp => "V_Tongue_up",
            CcViseme.TongueRaise => "V_Tongue_Raise",
            CcViseme.TongueOut => "V_Tongue_Out",
            CcViseme.TongueNarrow => "V_Tongue_Narrow",
            CcViseme.TongueLower => "V_Tongue_Lower",
            CcViseme.TongueCurlU => "V_Tongue_Curl_U",
            CcViseme.TongueCurlD => "V_Tongue_Curl_D",
            _ => throw new ArgumentOutOfRangeException(nameof(viseme), viseme, null),
        };

        /// <summary>
        /// The starting table, one entry per Oculus viseme in Oculus order, chosen by eye against the
        /// SMPL-X body's native Oculus visemes in <c>LipSyncTest</c> (Aaron, CC5); expect to adjust it
        /// per character. What the CC lip shapes look like there: <c>V_Lip_Open</c> parts the lips over
        /// closed teeth, <c>V_Wide</c> spreads them with little gap, <c>V_Affricate</c> flares them
        /// forward, <c>V_Tight_O</c> puckers. <c>V_Tight</c> presses the closed lips together, which no
        /// Oculus viseme asks for, so it is left out (PP uses <c>V_Explosive</c>).
        /// </summary>
        public static OculusVisemeBlend[] DefaultBlends() => new[]
        {
            new OculusVisemeBlend("sil"),
            new OculusVisemeBlend("PP", W(CcViseme.Explosive, 1f)),
            new OculusVisemeBlend("FF", W(CcViseme.DentalLip, 1f)),
            new OculusVisemeBlend("TH", W(CcViseme.LipOpen, 0.8f), W(CcViseme.Open, 0.15f), W(CcViseme.TongueOut, 1f)),
            new OculusVisemeBlend("DD", W(CcViseme.LipOpen, 1f), W(CcViseme.Open, 0.1f), W(CcViseme.TongueUp, 1f)),
            new OculusVisemeBlend("kk", W(CcViseme.LipOpen, 0.8f), W(CcViseme.Open, 0.3f), W(CcViseme.TongueRaise, 1f)),
            new OculusVisemeBlend("CH", W(CcViseme.Affricate, 1f), W(CcViseme.TongueUp, 0.4f)),
            new OculusVisemeBlend("SS", W(CcViseme.LipOpen, 1f), W(CcViseme.Wide, 0.3f), W(CcViseme.TongueNarrow, 0.6f)),
            new OculusVisemeBlend("nn", W(CcViseme.LipOpen, 0.8f), W(CcViseme.Open, 0.1f), W(CcViseme.TongueUp, 1f)),
            new OculusVisemeBlend("RR", W(CcViseme.TightO, 0.5f), W(CcViseme.Affricate, 0.5f), W(CcViseme.TongueCurlU, 1f)),
            new OculusVisemeBlend("aa", W(CcViseme.Open, 1f), W(CcViseme.TongueLower, 0.5f)),
            new OculusVisemeBlend("E", W(CcViseme.Wide, 0.8f), W(CcViseme.LipOpen, 0.5f), W(CcViseme.Open, 0.35f)),
            new OculusVisemeBlend("ih", W(CcViseme.Wide, 1f), W(CcViseme.LipOpen, 0.5f), W(CcViseme.Open, 0.15f)),
            new OculusVisemeBlend("oh", W(CcViseme.TightO, 0.8f), W(CcViseme.Open, 0.5f)),
            new OculusVisemeBlend("ou", W(CcViseme.TightO, 1f), W(CcViseme.Open, 0.1f)),
        };

        /// <summary>
        /// Writes the CC viseme weights (indexed by <see cref="CcViseme"/>, each in [0, 1]) for one
        /// frame of Oculus viseme scores (indexed in Oculus order, each in [0, 1]).
        /// </summary>
        public static void Mix(IReadOnlyList<OculusVisemeBlend> blends, ReadOnlySpan<float> visemes, Span<float> ccWeights)
        {
            ccWeights.Clear();
            var count = Math.Min(blends.Count, visemes.Length);
            for (var v = 0; v < count; v++)
            {
                var score = visemes[v];
                var shapes = blends[v].Shapes;
                if (score <= 0f || shapes == null) continue;
                foreach (var s in shapes)
                    ccWeights[(int)s.Shape] += score * s.Weight;
            }
            for (var i = 0; i < ccWeights.Length; i++)
                ccWeights[i] = Math.Clamp(ccWeights[i], 0f, 1f);
        }

        static CcShapeWeight W(CcViseme shape, float weight) => new(shape, weight);
    }
}
