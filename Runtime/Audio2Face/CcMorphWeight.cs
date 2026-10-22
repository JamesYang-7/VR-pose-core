using System;
using UnityEngine;

namespace VrChat.Audio2Face
{
    /// <summary>One Character Creator morph (blendshape) and how much of it a pose contributes at full strength.</summary>
    [Serializable]
    public struct CcMorphWeight
    {
        public string Morph;

        [Range(0f, 1f)]
        public float Weight;

        public CcMorphWeight(string morph, float weight)
        {
            Morph = morph;
            Weight = weight;
        }
    }
}
