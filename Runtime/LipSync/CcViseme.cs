namespace VrChat.LipSync
{
    /// <summary>
    /// The 15 Character Creator visemes: 8 lip shapes on <c>CC_Base_Body</c> and 7 tongue shapes on
    /// <c>CC_Base_Tongue</c>. <see cref="CcVisemeMixer.BlendShapeName"/> gives each one's blendshape name.
    /// </summary>
    public enum CcViseme
    {
        Open,
        Explosive,
        DentalLip,
        TightO,
        Tight,
        Wide,
        Affricate,
        LipOpen,
        TongueUp,
        TongueRaise,
        TongueOut,
        TongueNarrow,
        TongueLower,
        TongueCurlU,
        TongueCurlD,
    }
}
