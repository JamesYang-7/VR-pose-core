using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VrChat.Audio2Face
{
    /// <summary>
    /// One live Audio2Face stream: 16 kHz mono audio in, per-frame blendshape weights out.
    ///
    /// <para>Wraps a native session of <c>a2f_unity.dll</c>, which runs the regression model and its
    /// blendshape solver on a worker thread. <see cref="PushAudio"/> is safe to call from Unity's audio
    /// thread; <see cref="TryPopFrame"/> is meant for the main thread. A frame for audio sample t is
    /// ready roughly 0.27 s after sample t was pushed: the model looks that far ahead.</para>
    ///
    /// <para>The weights are <see cref="PoseNames"/> in order: the SDK's ARKit-style skin poses
    /// (<c>jawOpen</c>, <c>mouthSmileLeft</c>, …), then its tongue poses (<c>tongueTipUp</c>, …).</para>
    /// </summary>
    public sealed class Audio2FaceSession : IDisposable
    {
        readonly Audio2FaceNative.SessionHandle _handle;

        /// <summary>Sample rate <see cref="PushAudio"/> expects (16000).</summary>
        public int SampleRate { get; }

        /// <summary>Name of each weight in a frame.</summary>
        public IReadOnlyList<string> PoseNames { get; }

        /// <summary>How many of <see cref="PoseNames"/> are skin poses; the rest are tongue poses.</summary>
        public int SkinPoseCount { get; }

        Audio2FaceSession(Audio2FaceNative.SessionHandle handle)
        {
            _handle = handle;
            SampleRate = Audio2FaceNative.a2f_sample_rate(handle);
            SkinPoseCount = Audio2FaceNative.a2f_skin_weight_count(handle);
            var names = new string[Audio2FaceNative.a2f_weight_count(handle)];
            var buffer = new byte[256];
            for (var i = 0; i < names.Length; i++)
            {
                var length = Audio2FaceNative.a2f_weight_name(handle, i, buffer, buffer.Length);
                names[i] = Encoding.UTF8.GetString(buffer, 0, Math.Clamp(length, 0, buffer.Length - 1));
            }
            PoseNames = names;
        }

        /// <summary>
        /// Checks that <c>audio2x.dll</c> and its CUDA and TensorRT DLLs load, without loading a model.
        /// </summary>
        /// <param name="error">Which DLL is missing, when it returns false.</param>
        public static bool TryCheckRuntime(out string error)
        {
            try
            {
                var buffer = new byte[2048];
                var ok = Audio2FaceNative.a2f_check_runtime(buffer, buffer.Length) == 0;
                error = ok ? null : Decode(buffer);
                return ok;
            }
            catch (DllNotFoundException e)
            {
                error = $"a2f_unity.dll was not found ({e.Message}). Build and install it with Tools~/audio2face_plugin.py build.";
                return false;
            }
        }

        /// <summary>
        /// Loads a regression model (its <c>model.json</c>) and starts a stream at <paramref name="frameRate"/> fps.
        /// Takes about half a second; call it off the main thread.
        /// </summary>
        /// <exception cref="Audio2FaceException">The runtime or the model could not be loaded.</exception>
        public static Audio2FaceSession Open(string modelJsonPath, int frameRate)
        {
            if (!TryCheckRuntime(out var runtimeError)) throw new Audio2FaceException(runtimeError);
            var handle = Audio2FaceNative.a2f_create(modelJsonPath, frameRate, 1);
            if (handle.IsInvalid) throw new Audio2FaceException(LastError());
            return new Audio2FaceSession(handle);
        }

        /// <summary>Queues mono samples at <see cref="SampleRate"/>. Never blocks on inference.</summary>
        /// <returns>False if the session has failed; <see cref="TryPopFrame"/> then throws with the reason.</returns>
        public bool PushAudio(ReadOnlySpan<float> samples)
        {
            if (samples.IsEmpty) return true;
            return Audio2FaceNative.a2f_push_audio(_handle, ref MemoryMarshal.GetReference(samples), samples.Length) == 0;
        }

        /// <summary>Pops the oldest pending frame.</summary>
        /// <param name="weights">At least <see cref="PoseNames"/>.Count long.</param>
        /// <param name="timestamp">Audio sample index the frame belongs to.</param>
        /// <returns>True if a frame was written, false if none is pending.</returns>
        /// <exception cref="Audio2FaceException">The session has failed.</exception>
        public bool TryPopFrame(float[] weights, out long timestamp)
        {
            var status = Audio2FaceNative.a2f_pop_frame(_handle, weights, weights.Length, out timestamp);
            if (status < 0) throw new Audio2FaceException(LastError());
            return status == 1;
        }

        public Audio2FaceStats GetStats()
        {
            Audio2FaceNative.a2f_get_stats(_handle, out var s);
            return new Audio2FaceStats(s.PushedSamples, s.RetainedSamples, s.ProducedFrames, s.DroppedFrames, s.LatestFrameTimestamp);
        }

        /// <summary>Stops the worker thread and frees the model.</summary>
        public void Dispose() => _handle.Dispose();

        static string LastError()
        {
            var buffer = new byte[2048];
            Audio2FaceNative.a2f_last_error(buffer, buffer.Length);
            return Decode(buffer);
        }

        static string Decode(byte[] buffer)
        {
            var length = Array.IndexOf(buffer, (byte)0);
            return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
        }
    }
}
