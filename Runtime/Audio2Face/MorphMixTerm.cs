namespace VrChat.Audio2Face
{
    /// <summary>One resolved entry of a pose → morph table: weight[Pose] × Weight is added to morph[Morph].</summary>
    public readonly struct MorphMixTerm
    {
        public int Pose { get; }

        public int Morph { get; }

        public float Weight { get; }

        public MorphMixTerm(int pose, int morph, float weight)
        {
            Pose = pose;
            Morph = morph;
            Weight = weight;
        }
    }
}
