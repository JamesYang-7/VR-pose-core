using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VrChat.Audio2Face;

namespace VrChat.Testing.Audio2Face
{
    [TestFixture]
    [Category("Integration")]
    public class Audio2FaceSessionTests
    {
        const int FrameRate = 60;

        static string ModelPath => Path.Combine(Application.streamingAssetsPath, "Audio2Face/james/model.json");

        static Audio2FaceSession OpenInstalledModel()
        {
            Assume.That(ModelPath, Does.Exist, "install it with Tools~/audio2face_plugin.py install-model");
            var runtimeOk = Audio2FaceSession.TryCheckRuntime(out var error);
            Assume.That(runtimeOk, Is.True, error);
            return Audio2FaceSession.Open(ModelPath, FrameRate);
        }

        static long WaitForFrames(Audio2FaceSession session, long count, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            while (session.GetStats().ProducedFrames < count && clock.ElapsedMilliseconds < timeoutMs)
                System.Threading.Thread.Sleep(5);
            return session.GetStats().ProducedFrames;
        }

        [Test]
        public void Open_InstalledModel_Exposes52SkinAnd16TonguePoses()
        {
            using var sut = OpenInstalledModel();

            Assert.That(sut.SkinPoseCount, Is.EqualTo(52), "skin poses");
            Assert.That(sut.PoseNames.Count, Is.EqualTo(68), "all poses");
            Assert.That(sut.PoseNames, Has.Member("jawOpen"), "jawOpen present");
        }

        [Test]
        public void PushAudio_TwoSecondsOfSilence_DeliversAFrameForEverySixtiethOfTheFirstSecond()
        {
            using var sut = OpenInstalledModel();

            sut.PushAudio(new float[2 * sut.SampleRate]);
            var actual = WaitForFrames(sut, FrameRate, 10000);

            Assert.That(actual, Is.GreaterThanOrEqualTo(FrameRate));
        }

        [Test]
        public void Open_MissingModel_ThrowsAudio2FaceException()
        {
            var runtimeOk = Audio2FaceSession.TryCheckRuntime(out var error);
            Assume.That(runtimeOk, Is.True, error);

            Assert.That(() => Audio2FaceSession.Open(Path.Combine(Application.temporaryCachePath, "no-such-model.json"), FrameRate),
                Throws.TypeOf<Audio2FaceException>());
        }
    }
}
