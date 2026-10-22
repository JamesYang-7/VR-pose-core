using System.IO;
using NUnit.Framework;

namespace VrChat.Testing
{
    /// <summary>
    /// Where the model exports' graphs and reference data live (<see cref="ModelExport"/>), and the guards for tests
    /// that need them. They are git-ignored and come from the project's local-files zip (its README.md, "Local files zips"), so on
    /// a bare clone the tests that need them are skipped with a pointer instead of failing.
    /// </summary>
    static class ModelLocalFiles
    {
        /// <summary>RPM's default version: its graph and its manifest (tracked).</summary>
        public static string RpmModelAssetPath => $"{ModelExport.ModelsAssetPath}/rpm/models/{ModelExport.Rpm.DefaultModel}/rpm_{ModelExport.Rpm.DefaultModel}.onnx";
        public static string RpmManifestPath => Path.Combine(ModelExport.Rpm.ModelDir(), "manifest.json");

        /// <summary>Every model export, the parent of <c>rpm/</c>.</summary>
        public static string ModelsDir => ModelExport.ModelsDir;
        public static string FingerprintPath => Path.Combine(ModelsDir, "model_fingerprint.txt");

        const string kHowToGet = "extract the project's local-files zip at its root (its README.md, \"Local files zips\")";

        /// <summary>Skips the test unless RPM's default version's graph, the captures and its reference output are present.</summary>
        public static void RequireRpm() => Require(ModelExport.Rpm, $"rpm_{ModelExport.Rpm.DefaultModel}.onnx");

        static void Require(ModelExport export, string graph)
        {
            if (!File.Exists(Path.Combine(export.ModelDir(), graph)) || !Directory.Exists(export.DataDir)
                || !Directory.Exists(Path.Combine(export.ModelDir(), "output_ref")))
                Assert.Ignore($"The {export.Folder} model files are not present: {kHowToGet}.");
        }
    }
}
