using System;

namespace VrChat.Audio2Face
{
    /// <summary>A failure reported by the Audio2Face native plugin.</summary>
    public sealed class Audio2FaceException : Exception
    {
        public Audio2FaceException(string message) : base(message) { }
    }
}
