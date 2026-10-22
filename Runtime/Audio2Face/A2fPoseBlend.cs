using System;

namespace VrChat.Audio2Face
{
    /// <summary>What one Audio2Face pose becomes on a Character Creator character: a few weighted morphs.</summary>
    [Serializable]
    public struct A2fPoseBlend
    {
        /// <summary>The Audio2Face pose name, e.g. <c>jawOpen</c> or <c>tongueTipUp</c>.</summary>
        public string Pose;

        public CcMorphWeight[] Morphs;

        public A2fPoseBlend(string pose, params CcMorphWeight[] morphs)
        {
            Pose = pose;
            Morphs = morphs;
        }
    }
}
