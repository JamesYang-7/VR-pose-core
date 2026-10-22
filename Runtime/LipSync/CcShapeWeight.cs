using System;
using UnityEngine;

namespace VrChat.LipSync
{
    /// <summary>One CC viseme and how much of it an Oculus viseme contributes at full strength.</summary>
    [Serializable]
    public struct CcShapeWeight
    {
        public CcViseme Shape;

        [Range(0f, 1f)]
        public float Weight;

        public CcShapeWeight(CcViseme shape, float weight)
        {
            Shape = shape;
            Weight = weight;
        }
    }
}
