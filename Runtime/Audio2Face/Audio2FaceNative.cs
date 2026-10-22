using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VrChat.Audio2Face
{
    /// <summary>
    /// P/Invoke declarations for <c>a2f_unity.dll</c> (source: <c>Native~/Audio2FacePlugin/</c>, built
    /// against the NVIDIA Audio2X SDK). See <c>a2f_unity.h</c> for the contract of each call.
    /// </summary>
    static class Audio2FaceNative
    {
        const string Dll = "a2f_unity";

        [StructLayout(LayoutKind.Sequential)]
        internal struct Stats
        {
            public long PushedSamples;
            public long RetainedSamples;
            public long ProducedFrames;
            public long DroppedFrames;
            public long LatestFrameTimestamp;
        }

        /// <summary>Owns a native session; releasing it calls <c>a2f_destroy</c>.</summary>
        /// <remarks>
        /// A SafeHandle rather than an IntPtr: the marshaller holds a reference for the duration of every
        /// call, so a session disposed on the main thread is not freed while the audio thread is still
        /// inside <c>a2f_push_audio</c> with it.
        /// </remarks>
        internal sealed class SessionHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SessionHandle() : base(true) { }

            protected override bool ReleaseHandle()
            {
                a2f_destroy(handle);
                return true;
            }
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_check_runtime(byte[] message, int messageSize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern SessionHandle a2f_create(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string modelJsonPath, int frameRateNumerator, int frameRateDenominator);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern void a2f_destroy(System.IntPtr session);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_sample_rate(SessionHandle session);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_weight_count(SessionHandle session);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_skin_weight_count(SessionHandle session);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_weight_name(SessionHandle session, int index, byte[] buffer, int bufferSize);

        // `ref float` (the first sample) instead of float[] so a Span can be pushed without unsafe code;
        // the marshaller pins it for the call.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_push_audio(SessionHandle session, ref float samples, int count);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_pop_frame(SessionHandle session, float[] weights, int capacity, out long timestamp);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_get_stats(SessionHandle session, out Stats stats);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int a2f_last_error(byte[] buffer, int bufferSize);
    }
}
