using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VrChat
{
    /// <summary>A C-order, little-endian float32 array loaded from a NumPy .npy file.</summary>
    public sealed class NpyArray
    {
        public readonly float[] Data;   // flat, C-order (row-major)
        public readonly int[] Shape;

        public NpyArray(float[] data, int[] shape) { Data = data; Shape = shape; }

        public long Length => Data.LongLength;
        public string ShapeString() => "(" + string.Join(", ", Shape) + ")";
    }

    /// <summary>
    /// Minimal NumPy .npy reader for C-order little-endian float32 arrays
    /// (the format produced by <c>np.save(arr.astype('&lt;f4'))</c>).
    ///
    /// Robustness over convenience: the header (dtype, fortran_order, shape) is
    /// parsed and validated, so a fixture saved with the wrong dtype/order fails
    /// loudly with a descriptive message instead of silently loading garbage.
    /// Supports .npy format versions 1.0, 2.0 and 3.0.
    /// </summary>
    public static class NpyReader
    {
        static readonly byte[] Magic = { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y' };

        public static NpyArray LoadFloat32(string path)
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(path); }
            catch (Exception e) { throw new IOException($"npy: cannot read '{path}': {e.Message}", e); }
            return LoadFloat32(raw, path);
        }

        /// <summary>
        /// Parses the bytes of a .npy file (e.g. one member of an .npz, see <see cref="NpzArchive"/>).
        /// <paramref name="path"/> only names the source in error messages.
        /// </summary>
        public static NpyArray LoadFloat32(byte[] raw, string path)
        {
            if (raw.Length < 10)
                throw new InvalidDataException($"npy: '{path}' too short ({raw.Length} bytes)");
            for (int i = 0; i < Magic.Length; i++)
                if (raw[i] != Magic[i])
                    throw new InvalidDataException($"npy: '{path}' bad magic; not a .npy file");

            int major = raw[6];
            int headerLen, headerStart;
            if (major == 1)
            {
                headerLen = raw[8] | (raw[9] << 8);                                   // uint16 LE
                headerStart = 10;
            }
            else // 2.x / 3.x use a uint32 LE header length
            {
                headerLen = raw[8] | (raw[9] << 8) | (raw[10] << 16) | (raw[11] << 24);
                headerStart = 12;
            }
            if (headerStart + (long)headerLen > raw.Length)
                throw new InvalidDataException($"npy: '{path}' header length out of range");

            string header = Encoding.ASCII.GetString(raw, headerStart, headerLen);

            string descr = MatchField(header, @"'descr'\s*:\s*'([^']+)'", path, "descr");
            string fortran = MatchField(header, @"'fortran_order'\s*:\s*(True|False)", path, "fortran_order");
            string shapeStr = MatchField(header, @"'shape'\s*:\s*\(([^)]*)\)", path, "shape");

            // dtype must be little-endian (or native, which is LE on supported platforms) float32.
            if (descr.IndexOf("f4", StringComparison.Ordinal) < 0 || descr.StartsWith(">", StringComparison.Ordinal))
                throw new NotSupportedException(
                    $"npy: '{path}' dtype '{descr}' unsupported; expected little-endian float32 ('<f4'). " +
                    "Re-save with arr.astype('<f4').");
            if (fortran == "True")
                throw new NotSupportedException(
                    $"npy: '{path}' is Fortran-ordered; expected C-order. Re-save with np.ascontiguousarray(arr).");

            int[] shape = ParseShape(shapeStr);
            long count = 1;
            foreach (int d in shape) count *= d;
            if (count * 4L > int.MaxValue)
                throw new NotSupportedException($"npy: '{path}' has {count} elements; too large for this reader.");

            long dataBytes = raw.LongLength - (headerStart + (long)headerLen);
            if (dataBytes < count * 4L)
                throw new InvalidDataException(
                    $"npy: '{path}' data block {dataBytes} bytes < expected {count * 4L} for shape {Show(shape)}");

            var data = new float[count];
            // x86/ARM little-endian + '<f4' little-endian => raw bytes map straight onto float32.
            Buffer.BlockCopy(raw, headerStart + headerLen, data, 0, (int)(count * 4L));
            return new NpyArray(data, shape);
        }

        static string MatchField(string header, string pattern, string path, string what)
        {
            var m = Regex.Match(header, pattern);
            if (!m.Success)
                throw new InvalidDataException($"npy: '{path}' header missing '{what}'. Header: {header.Trim()}");
            return m.Groups[1].Value;
        }

        static int[] ParseShape(string inner)
        {
            inner = inner.Trim();
            if (inner.Length == 0) return Array.Empty<int>(); // 0-d scalar
            var parts = inner.Split(',');
            var dims = new List<int>(parts.Length);
            foreach (var p in parts)
            {
                var s = p.Trim();
                if (s.Length == 0) continue; // tolerate trailing comma, e.g. "(5,)"
                dims.Add(int.Parse(s, CultureInfo.InvariantCulture));
            }
            return dims.ToArray();
        }

        static string Show(int[] shape) => "(" + string.Join(", ", shape) + ")";
    }
}
