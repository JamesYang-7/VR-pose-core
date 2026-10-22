using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VrChat.Testing
{
    /// <summary>
    /// Writes small .npz files the way NumPy does (a zip of .npy members), built byte by byte here
    /// rather than with <see cref="NpyWriter"/>, so the reader is checked against the format and not
    /// against our own writer. Files go to a per-test temp folder that <see cref="Clean"/> removes.
    /// </summary>
    static class NpzTestFiles
    {
        static string Dir => Path.Combine(Application.temporaryCachePath, "NpzTestFiles");

        public static void Clean()
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        }

        /// <summary>A .npy member: <paramref name="shape"/> is NumPy's literal, e.g. "(2, 3)", "(4,)" or "()".</summary>
        public static byte[] Npy(float[] data, string shape, string descr = "<f4")
        {
            string dict = $"{{'descr': '{descr}', 'fortran_order': False, 'shape': {shape}, }}";
            int pad = (64 - (10 + dict.Length + 1) % 64) % 64;
            byte[] header = Encoding.ASCII.GetBytes(dict + new string(' ', pad) + "\n");
            var bytes = new List<byte> { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0,
                (byte)(header.Length & 0xFF), (byte)(header.Length >> 8) };
            bytes.AddRange(header);
            var payload = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, payload, 0, payload.Length);
            bytes.AddRange(payload);
            return bytes.ToArray();
        }

        /// <summary>Zips <paramref name="members"/> (name → .npy bytes) into <paramref name="fileName"/>.</summary>
        public static string Write(string fileName, IReadOnlyDictionary<string, byte[]> members,
            CompressionLevel level = CompressionLevel.Optimal)
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, fileName);
            using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
            foreach (var m in members)
            {
                using var stream = zip.CreateEntry(m.Key + ".npy", level).Open();
                stream.Write(m.Value, 0, m.Value.Length);
            }
            return path;
        }

        public static float[] Ramp(int count, float start = 0f)
        {
            var a = new float[count];
            for (int i = 0; i < count; i++) a[i] = start + i;
            return a;
        }
    }
}
