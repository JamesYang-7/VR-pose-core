using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace VrChat
{
    /// <summary>
    /// A NumPy .npz archive — what <c>np.savez</c> / <c>np.savez_compressed</c> write: a ZIP whose
    /// members are .npy files, named by the keyword each array was saved under. Every member is
    /// loaded eagerly through <see cref="NpyReader"/>, so all must be little-endian float32 (scalars
    /// such as a frame rate included: save them as <c>np.float32(60)</c>, not a Python int).
    ///
    /// <para>Lets one file carry a whole sample — e.g. a model sequence's sparse input and reference
    /// rotations — instead of one .npy per tensor.</para>
    /// </summary>
    public sealed class NpzArchive
    {
        const string kMemberSuffix = ".npy";

        readonly Dictionary<string, NpyArray> _members;

        /// <summary>The file this archive was read from (used in error messages).</summary>
        public string Path { get; }

        /// <summary>The member names, without the <c>.npy</c> suffix.</summary>
        public IReadOnlyCollection<string> Names => _members.Keys;

        NpzArchive(string path, Dictionary<string, NpyArray> members)
        {
            Path = path;
            _members = members;
        }

        /// <summary>Reads every .npy member of the archive at <paramref name="path"/>.</summary>
        public static NpzArchive Load(string path)
        {
            var members = new Dictionary<string, NpyArray>();
            try
            {
                using var file = File.OpenRead(path); // owned here: ZipArchive's ctor throws before taking it on a bad file
                using var zip = new ZipArchive(file, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith(kMemberSuffix, StringComparison.Ordinal)) continue;
                    using var stream = entry.Open();
                    using var bytes = new MemoryStream((int)entry.Length);
                    stream.CopyTo(bytes);
                    string name = entry.FullName.Substring(0, entry.FullName.Length - kMemberSuffix.Length);
                    members[name] = NpyReader.LoadFloat32(bytes.ToArray(), $"{path}:{entry.FullName}");
                }
            }
            catch (InvalidDataException e) when (e.Message.IndexOf("npy:", StringComparison.Ordinal) < 0)
            {
                throw new InvalidDataException($"npz: '{path}' is not a readable zip archive: {e.Message}", e);
            }
            return new NpzArchive(path, members);
        }

        /// <summary>True if the archive has a member saved under <paramref name="name"/>.</summary>
        public bool Contains(string name) => _members.ContainsKey(name);

        /// <summary>The member saved under <paramref name="name"/>.</summary>
        /// <exception cref="KeyNotFoundException">No such member; the message lists the ones present.</exception>
        public NpyArray this[string name] => _members.TryGetValue(name, out var array)
            ? array
            : throw new KeyNotFoundException($"npz: '{Path}' has no member '{name}' (members: {string.Join(", ", _members.Keys)})");
    }
}
