namespace VrChat.Audio2Face
{
    /// <summary>Counters of an <see cref="Audio2FaceSession"/>, for diagnostics.</summary>
    public readonly struct Audio2FaceStats
    {
        /// <summary>Samples received by <see cref="Audio2FaceSession.PushAudio"/>.</summary>
        public long PushedSamples { get; }

        /// <summary>Samples the SDK still holds; stays around 1–2 s of audio while the session keeps up.</summary>
        public long RetainedSamples { get; }

        /// <summary>Frames the SDK has delivered.</summary>
        public long ProducedFrames { get; }

        /// <summary>Frames discarded because nobody popped them.</summary>
        public long DroppedFrames { get; }

        /// <summary>Audio sample index of the newest delivered frame, -1 before the first.</summary>
        public long LatestFrameTimestamp { get; }

        public Audio2FaceStats(long pushedSamples, long retainedSamples, long producedFrames, long droppedFrames, long latestFrameTimestamp)
        {
            PushedSamples = pushedSamples;
            RetainedSamples = retainedSamples;
            ProducedFrames = producedFrames;
            DroppedFrames = droppedFrames;
            LatestFrameTimestamp = latestFrameTimestamp;
        }
    }
}
