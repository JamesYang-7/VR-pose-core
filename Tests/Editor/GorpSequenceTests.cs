using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace VrChat.Testing
{
    [TestFixture]
    public class GorpSequenceTests
    {
        const int kFrames = 3;

        [SetUp]
        public void SetUp() => NpzTestFiles.Clean();

        [TearDown]
        public void TearDown() => NpzTestFiles.Clean();

        // data/gorp/<capture>.npz
        static Dictionary<string, byte[]> ValidData() => new()
        {
            ["sparse_raw"] = NpzTestFiles.Npy(new float[(kFrames + 1) * 24], $"({kFrames + 1}, 3, 8)"),
            ["gt_motion"] = NpzTestFiles.Npy(new float[kFrames * 198], $"({kFrames}, 198)"),
            ["joints_rest"] = NpzTestFiles.Npy(new float[66], "(22, 3)"),
        };

        // models/<name>/output_ref/<capture>.npz
        static Dictionary<string, byte[]> ValidOutputRef(int frames = kFrames) => new()
        {
            ["sparse_ctx"] = NpzTestFiles.Npy(new float[frames * 54], $"({frames}, 54)"),
            ["ref_pred_motion"] = NpzTestFiles.Npy(new float[frames * 198], $"({frames}, 198)"),
        };

        static GorpSequence Load(Dictionary<string, byte[]> data, Dictionary<string, byte[]> outputRef) =>
            GorpSequence.Load(NpzTestFiles.Write("s99_test.npz", data), NpzTestFiles.Write("s99_test_ref.npz", outputRef));

        [Test]
        public void Load_ValidMembers_ReadsTheFrameCountAndTheCaptureName()
        {
            var sut = Load(ValidData(), ValidOutputRef());

            Assert.That(sut.FrameCount, Is.EqualTo(kFrames), "frames");
            Assert.That(sut.Name, Is.EqualTo("s99_test"), "name");
        }

        [Test]
        public void Load_SparseRawOneRowShort_ThrowsInvalidDataException()
        {
            var data = ValidData();
            data["sparse_raw"] = NpzTestFiles.Npy(new float[kFrames * 24], $"({kFrames}, 3, 8)");

            Assert.That(() => Load(data, ValidOutputRef()), Throws.TypeOf<InvalidDataException>().With.Message.Contains("'sparse_raw'"));
        }

        [Test]
        public void Load_GroundTruthMissing_ThrowsInvalidDataException()
        {
            var data = ValidData();
            data.Remove("gt_motion");

            Assert.That(() => Load(data, ValidOutputRef()), Throws.TypeOf<InvalidDataException>().With.Message.Contains("missing member 'gt_motion'"));
        }

        [Test]
        public void Load_OutputRefOfAnotherLength_ThrowsInvalidDataException()
        {
            Assert.That(() => Load(ValidData(), ValidOutputRef(kFrames + 1)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("'sparse_ctx'"));
        }

        static IEnumerable<TestCaseData> BundleSequences()
        {
            foreach (var name in ModelExport.Rpm.SequenceNames())
                yield return new TestCaseData(name).SetName($"Load_BundleSequence_IsValid({name})");
        }

        [TestCaseSource(nameof(BundleSequences))]
        public void Load_BundleSequence_IsValid(string name)
        {
            var actual = ModelExport.Rpm.LoadSequence(name);

            Assert.That(actual.FrameCount, Is.GreaterThan(GorpSequence.Fps), "frames: longer than a second");
        }
    }
}
