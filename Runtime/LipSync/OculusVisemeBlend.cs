using System;

namespace VrChat.LipSync
{
    /// <summary>The CC shapes one Oculus viseme becomes.</summary>
    [Serializable]
    public struct OculusVisemeBlend
    {
        // Label only; Unity names the array element in the Inspector after the first string field.
        public string Viseme;

        public CcShapeWeight[] Shapes;

        public OculusVisemeBlend(string viseme, params CcShapeWeight[] shapes)
        {
            Viseme = viseme;
            Shapes = shapes;
        }
    }
}
