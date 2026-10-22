using System;
using System.IO;
using System.Text;

namespace VrChat
{
    /// <summary>
    /// Minimal NumPy .npy writer for C-order little-endian float32 arrays — the inverse of
    /// <see cref="NpyReader"/>, producing files that both it and <c>numpy.load</c> read back
    /// verbatim. Writes format version 1.0 (uint16 header length), which covers any array
    /// whose header fits in 64 KB (always true here).
    /// </summary>
    public static class NpyWriter
    {
        static readonly byte[] Magic = { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y' };

        /// <summary>
        /// Save <paramref name="data"/> (flat, C-order) as a float32 .npy at <paramref name="path"/>
        /// with the given <paramref name="shape"/>. Creates the directory if needed.
        /// </summary>
        public static void SaveFloat32(string path, float[] data, int[] shape)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (shape == null || shape.Length == 0) throw new ArgumentException("shape must have at least one dimension", nameof(shape));

            long count = 1;
            foreach (int d in shape) count *= d;
            if (count != data.LongLength)
                throw new ArgumentException($"shape {Show(shape)} = {count} elements != data length {data.LongLength}");

            // numpy renders 1-D shapes with a trailing comma: "(N,)".
            string shapeStr = shape.Length == 1 ? $"({shape[0]},)" : "(" + string.Join(", ", shape) + ")";
            string dict = "{'descr': '<f4', 'fortran_order': False, 'shape': " + shapeStr + ", }";

            // Pad the header with spaces so (magic+version+lenField + header) is a multiple of
            // 64, and terminate with '\n' — required by the .npy spec.
            const int preamble = 10; // 6 magic + 2 version + 2 uint16 length
            int unpadded = preamble + dict.Length + 1; // +1 for the trailing newline
            int pad = (64 - (unpadded % 64)) % 64;
            string header = dict + new string(' ', pad) + "\n";

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            fs.Write(Magic, 0, Magic.Length);
            fs.WriteByte(1); // version major
            fs.WriteByte(0); // version minor
            ushort headerLen = (ushort)header.Length;
            fs.WriteByte((byte)(headerLen & 0xFF));
            fs.WriteByte((byte)((headerLen >> 8) & 0xFF));
            var headerBytes = Encoding.ASCII.GetBytes(header);
            fs.Write(headerBytes, 0, headerBytes.Length);

            // x86/ARM little-endian + '<f4' little-endian => float32[] maps straight to bytes.
            var bytes = new byte[count * 4L];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            fs.Write(bytes, 0, bytes.Length);
        }

        static string Show(int[] shape) => "(" + string.Join(", ", shape) + ")";
    }
}
