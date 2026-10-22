using System;
using System.IO;
using System.Linq;

namespace VrChat
{
    /// <summary>
    /// Layout of a model export, e.g. this package's <c>Models/rpm/</c> (RPM-README, file tables):
    /// the GORP test captures every model version shares in <c>data/gorp/</c>, and per version
    /// <c>models/&lt;name&gt;/</c> with its step graph, its <c>manifest.json</c> and its reference output
    /// <c>output_ref/</c>. The captures, the graphs and the reference outputs are git-ignored and come from the
    /// local-files zip.
    /// </summary>
    public sealed class ModelExport
    {
        /// <summary>This package's name, as in its <c>package.json</c>.</summary>
        public const string PackageName = "io.github.jamesyang-7.vr-pose-core";

        /// <summary>The package's <c>Models</c> folder, the parent of every export, as an asset path.</summary>
        public const string ModelsAssetPath = "Packages/" + PackageName + "/Models";

        // Path.GetFullPath maps a Packages/<name> path to wherever the package really is (embedded, local or
        // git), so this needs no PackageManager call, which a runtime assembly cannot make.
        /// <summary>The package's <c>Models</c> folder on disk. Editor only: a player has no package folders.</summary>
        public static string ModelsDir => Path.GetFullPath(ModelsAssetPath);

        /// <summary>RPM's released checkpoints; its default version is the one trained on real device input.</summary>
        public static readonly ModelExport Rpm = new(null, "rpm", "gorp_real_inputs_reactive");

        readonly string _parentDir; // null: the package's Models folder

        /// <summary>The export's folder name.</summary>
        public string Folder { get; }

        /// <summary>The version <see cref="ModelDir"/> and <see cref="LoadSequence"/> use when none is named.</summary>
        public string DefaultModel { get; }

        /// <summary>
        /// An export laid out like this package's, in <paramref name="parentDir"/>/<paramref name="folder"/>: for
        /// exports a project keeps itself, e.g. under its <c>Assets/Models</c>. A null <paramref name="parentDir"/>
        /// means the package's <see cref="ModelsDir"/>.
        /// </summary>
        public ModelExport(string parentDir, string folder, string defaultModel)
        {
            _parentDir = parentDir;
            Folder = folder;
            DefaultModel = defaultModel;
        }

        /// <summary>The export's root folder.</summary>
        public string Root => Path.Combine(_parentDir ?? ModelsDir, Folder);

        /// <summary>The GORP captures shared by every model version (<c>data/gorp/&lt;capture&gt;.npz</c>).</summary>
        public string DataDir => Path.Combine(Root, "data", "gorp");

        /// <summary>One model version's folder: graph, manifest and <c>output_ref/</c>.</summary>
        public string ModelDir(string model = null) => Path.Combine(Root, "models", model ?? DefaultModel);

        /// <summary>The captures in <see cref="DataDir"/>, by file name without extension, sorted; empty if absent.</summary>
        public string[] SequenceNames() =>
            Directory.Exists(DataDir)
                ? Directory.GetFiles(DataDir, "*.npz").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

        /// <summary>Loads capture <paramref name="name"/> with <paramref name="model"/>'s reference output.</summary>
        public GorpSequence LoadSequence(string name, string model = null) =>
            GorpSequence.Load(Path.Combine(DataDir, name + ".npz"), Path.Combine(ModelDir(model), "output_ref", name + ".npz"));
    }
}
