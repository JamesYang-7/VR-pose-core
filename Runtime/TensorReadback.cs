using System;
using Unity.InferenceEngine;

namespace VrChat
{
    /// <summary>
    /// Collects a GPU tensor whose readback was requested (<c>ReadbackRequest</c>) and reported done
    /// (<c>IsReadbackRequestDone</c>): the step runners' last stage.
    ///
    /// <para>An async readback can complete with an error. It happens about once or twice in 30 s of RPM while
    /// Audio2Face's CUDA work shares the GPU. <c>DownloadToArray</c> then throws, having already dropped the failed
    /// request, so a second call reads the finished result synchronously: a short blocking copy instead of a lost
    /// step and an exception out of the runner.</para>
    /// </summary>
    public static class TensorReadback
    {
        /// <summary>Downloads that failed once and were read again, since the domain loaded.</summary>
        public static int Retries { get; private set; }

        /// <summary>The tensor's values. Throws only if the synchronous retry fails too.</summary>
        public static float[] Download(Tensor<float> tensor)
        {
            try
            {
                return tensor.DownloadToArray();
            }
            catch (InvalidOperationException)
            {
                Retries++;
                return tensor.DownloadToArray();
            }
        }
    }
}
