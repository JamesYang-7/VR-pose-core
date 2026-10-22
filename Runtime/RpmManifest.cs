using System;
using System.IO;
using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// What <see cref="RpmStepRunner"/> needs from an RPM version's <c>manifest.json</c> (RPM-README §3, §5): its frame
    /// rate and windows, and the normalisation statistics the state is seeded with.
    /// </summary>
    public sealed class RpmManifest
    {
        /// <summary>Floats per state row: 22 × 6D rotations, normalised.</summary>
        public const int RotationWidth = 132;

        /// <summary>The version's name, which is also its folder under <c>models/</c>.</summary>
        public string Name { get; }

        /// <summary>Model frame rate (30, or 60 for <c>amass_p1_reactive</c>).</summary>
        public float Fps { get; }

        /// <summary>A hand counts as tracked at or above this confidence.</summary>
        public float ConfidenceThreshold { get; }

        /// <summary>Input frames per step, <c>sparse_ctx</c>'s length: the newest frame and the ones before it.</summary>
        public int InputFrames { get; }

        /// <summary>Committed state rows per step, <c>motion_ctx</c>'s length.</summary>
        public int StateFrames { get; }

        /// <summary>L, the rolling prediction window, <c>prev_pred</c>'s length.</summary>
        public int PredictionWindow { get; }

        /// <summary>How many frames before the newest input frame the committed pose is; 0 for every released version.</summary>
        public int RollingLatency { get; }

        /// <summary>Per-dimension mean of the state rows.</summary>
        public float[] MotionMean { get; }

        /// <summary>Per-dimension standard deviation of the state rows (0 on the feet of the GORP and AMASS-P1 versions).</summary>
        public float[] MotionStd { get; }

        RpmManifest(Json j)
        {
            Name = j.name;
            Fps = j.fps;
            ConfidenceThreshold = j.input_conf_threshold;
            InputFrames = j.geometry.sparse_ctx_frames;
            StateFrames = j.geometry.motion_ctx_frames;
            PredictionWindow = j.geometry.prediction_window;
            RollingLatency = j.geometry.rolling_latency;
            MotionMean = j.normalisation.motion_mean;
            MotionStd = j.normalisation.motion_std;
        }

        /// <summary>Reads a version's manifest.</summary>
        /// <exception cref="InvalidDataException">A field the runner needs is missing or malformed.</exception>
        public static RpmManifest Parse(string json)
        {
            // JsonUtility creates every nested object, so a missing block shows as zeros and empty arrays, not null.
            var j = JsonUtility.FromJson<Json>(json);
            if (j == null || string.IsNullOrEmpty(j.name))
                throw new InvalidDataException("RPM manifest: no name (RPM-README §5).");
            if (j.normalisation.motion_mean?.Length != RotationWidth || j.normalisation.motion_std?.Length != RotationWidth)
                throw new InvalidDataException($"RPM manifest '{j.name}': normalisation.motion_mean and motion_std must hold {RotationWidth} floats.");
            if (j.fps <= 0 || j.geometry.sparse_ctx_frames <= 0 || j.geometry.motion_ctx_frames <= 0 || j.geometry.prediction_window <= 0)
                throw new InvalidDataException($"RPM manifest '{j.name}': fps and every geometry window must be positive.");
            return new RpmManifest(j);
        }

        /// <summary>
        /// <c>normalise(x) = (x − motion_mean) / (motion_std + 1e-8)</c> on one 132-float row, in float32 as RPM computes
        /// it. A dimension with zero deviation gives exactly 0 only for exactly its mean.
        /// </summary>
        public void Normalise(float[] src, int srcOffset, float[] dst, int dstOffset)
        {
            for (int k = 0; k < RotationWidth; k++)
                dst[dstOffset + k] = (src[srcOffset + k] - MotionMean[k]) / (MotionStd[k] + 1e-8f);
        }

        // Field names mirror manifest.json's keys, which JsonUtility matches by name.
        [Serializable]
        class Json
        {
            public string name;
            public float fps;
            public float input_conf_threshold;
            public Geometry geometry;
            public Normalisation normalisation;
        }

        [Serializable]
        class Geometry
        {
            public int sparse_ctx_frames;
            public int motion_ctx_frames;
            public int prediction_window;
            public int rolling_latency;
        }

        [Serializable]
        class Normalisation
        {
            public float[] motion_mean;
            public float[] motion_std;
        }
    }
}
