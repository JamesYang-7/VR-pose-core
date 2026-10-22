using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;

namespace VrChat.Testing
{
    [TestFixture]
    public class NpzArchiveTests
    {
        [SetUp]
        public void SetUp() => NpzTestFiles.Clean();

        [TearDown]
        public void TearDown() => NpzTestFiles.Clean();

        [Test]
        public void Load_StoredOrCompressedMembers_ReturnsEachArrayByName(
            [Values(CompressionLevel.NoCompression, CompressionLevel.Optimal)] CompressionLevel level)
        {
            var path = NpzTestFiles.Write("two.npz", new Dictionary<string, byte[]>
            {
                ["sparse"] = NpzTestFiles.Npy(NpzTestFiles.Ramp(6), "(2, 3)"),
                ["rot"] = NpzTestFiles.Npy(NpzTestFiles.Ramp(4, 100f), "(4,)"),
            }, level);

            var sut = NpzArchive.Load(path);

            Assert.That(sut["sparse"].Shape, Is.EqualTo(new[] { 2, 3 }), "sparse shape");
            Assert.That(sut["sparse"].Data, Is.EqualTo(NpzTestFiles.Ramp(6)), "sparse data");
            Assert.That(sut["rot"].Data, Is.EqualTo(NpzTestFiles.Ramp(4, 100f)), "rot data");
        }

        [Test]
        public void Load_ScalarMember_ReturnsOneValueWithEmptyShape()
        {
            var path = NpzTestFiles.Write("scalar.npz", new Dictionary<string, byte[]>
            {
                ["fps"] = NpzTestFiles.Npy(new[] { 60f }, "()"),
            });

            var actual = NpzArchive.Load(path)["fps"];

            Assert.That(actual.Shape, Is.Empty, "shape");
            Assert.That(actual.Data, Is.EqualTo(new[] { 60f }), "value");
        }

        [Test]
        public void Indexer_MissingMember_ThrowsKeyNotFoundExceptionListingTheMembers()
        {
            var path = NpzTestFiles.Write("one.npz", new Dictionary<string, byte[]>
            {
                ["sparse"] = NpzTestFiles.Npy(NpzTestFiles.Ramp(3), "(3,)"),
            });
            var sut = NpzArchive.Load(path);

            Assert.That(() => sut["rot"], Throws.TypeOf<KeyNotFoundException>().With.Message.Contains("members: sparse"));
        }

        [Test]
        public void Load_NonFloat32Member_ThrowsNotSupportedExceptionNamingTheMember()
        {
            var path = NpzTestFiles.Write("int.npz", new Dictionary<string, byte[]>
            {
                ["first_committed"] = NpzTestFiles.Npy(new[] { 0f, 0f }, "()", descr: "<i8"),
            });

            Assert.That(() => NpzArchive.Load(path), Throws.TypeOf<NotSupportedException>().With.Message.Contains("first_committed.npy"));
        }

        [Test]
        public void Load_NotAZipFile_ThrowsInvalidDataException()
        {
            var path = NpzTestFiles.Write("placeholder.npz", new Dictionary<string, byte[]>());
            File.WriteAllBytes(path, NpzTestFiles.Npy(NpzTestFiles.Ramp(3), "(3,)")); // a bare .npy renamed to .npz

            Assert.That(() => NpzArchive.Load(path), Throws.TypeOf<InvalidDataException>().With.Message.Contains("not a readable zip"));
        }
    }
}
