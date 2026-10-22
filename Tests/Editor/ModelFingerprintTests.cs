using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace VrChat.Testing
{
    /// <summary>
    /// Ties the git-ignored model files under <c>Models/</c> (the graphs, the captures and the reference
    /// outputs of the exports) to the commit that expects them. They travel in the local-files zip, outside git;
    /// <c>Models/model_fingerprint.txt</c> (committed, written by <c>Tools~/model_fingerprint.py</c>) records their
    /// SHA-256.
    ///
    /// A failure means the local models are not the ones this commit was built for: extract the zip that matches
    /// this commit, or, after deliberately installing a new export, rerun the fingerprint tool and commit it.
    /// </summary>
    [TestFixture]
    public class ModelFingerprintTests
    {
        [Test]
        public void LocalModelFiles_MatchTheCommittedFingerprint()
        {
            ModelLocalFiles.RequireRpm();
            var mismatched = new List<string>();

            foreach (var line in File.ReadAllLines(ModelLocalFiles.FingerprintPath))
            {
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var hash = line.Substring(0, 64);
                var rel = line.Substring(66);
                var path = Path.Combine(ModelLocalFiles.ModelsDir, rel);
                if (!File.Exists(path)) mismatched.Add($"{rel} (missing)");
                else if (Sha256(path) != hash) mismatched.Add($"{rel} (different contents)");
            }

            Assert.That(mismatched, Is.Empty);
        }

        static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
