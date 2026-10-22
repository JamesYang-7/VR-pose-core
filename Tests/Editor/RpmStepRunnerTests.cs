using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace VrChat.Testing
{
    /// <summary>
    /// The RPM rolling loop against <c>gorp_real_inputs_reactive</c>'s reference output on s26 (RPM-README §5–6): the
    /// evaluation's seed, the first step on frame 10, and each step's pose on the newest input frame.
    /// </summary>
    [TestFixture]
    public class RpmStepRunnerTests
    {
        const string kSequence = "s26_fruitninja-q3-controllers_000";
        const int kInputWidth = PoseLayout.InputWidth;
        const int kMotionWidth = PoseLayout.MotionWidth;

        // The default version's manifest is tracked, so these need no local files.
        static string ManifestJson() => File.ReadAllText(ModelLocalFiles.RpmManifestPath);
        static RpmManifest Manifest() => RpmManifest.Parse(ManifestJson());

        static RpmStepRunner NewRunner(RpmManifest manifest) =>
            new(AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelLocalFiles.RpmModelAssetPath), manifest);

        static float[] Slice(float[] a, int start, int count) => a[start..(start + count)];

        [Test]
        public void Parse_ReleasedManifest_ReadsTheWindowsAndTheStatistics()
        {
            var actual = Manifest();

            Assert.That(actual.Name, Is.EqualTo(ModelExport.Rpm.DefaultModel), "name");
            Assert.That(actual.Fps, Is.EqualTo(30f), "fps");
            Assert.That(actual.ConfidenceThreshold, Is.EqualTo(0.8f), "threshold");
            Assert.That((actual.InputFrames, actual.StateFrames, actual.PredictionWindow), Is.EqualTo((11, 10, 5)), "windows");
            Assert.That(actual.RollingLatency, Is.EqualTo(0), "rolling latency");
            Assert.That(actual.MotionMean.Length, Is.EqualTo(RpmManifest.RotationWidth), "mean");
            Assert.That(actual.MotionStd.Length, Is.EqualTo(RpmManifest.RotationWidth), "std");
        }

        [Test]
        public void Parse_NoNormalisation_ThrowsInvalidDataException()
        {
            const string json = "{\"name\": \"x\", \"fps\": 30, \"geometry\": {\"sparse_ctx_frames\": 11, \"motion_ctx_frames\": 10, \"prediction_window\": 5}}";

            Assert.That(() => RpmManifest.Parse(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("normalisation"));
        }

        [Test]
        public void StandingState_AnyHeading_NormalisesTheFeetToExactlyZero()
        {
            var manifest = Manifest();
            var frame = new float[kInputWidth];
            frame[0] = 0.6f; frame[1] = -0.8f; frame[5] = 1f; // head 6D: a turned, upright headset
            var motionCtx = new float[manifest.StateFrames * RpmManifest.RotationWidth];
            var prevPred = new float[manifest.PredictionWindow * RpmManifest.RotationWidth];

            RpmStepRunner.StandingState(frame, manifest, new float[kMotionWidth], motionCtx, prevPred);

            for (int r = 0; r < manifest.StateFrames; r++)
                Assert.That(Slice(motionCtx, r * RpmManifest.RotationWidth + 60, 12), Is.EqualTo(new float[12]), $"motion_ctx row {r}");
            for (int r = 0; r < manifest.PredictionWindow; r++)
                Assert.That(Slice(prevPred, r * RpmManifest.RotationWidth, RpmManifest.RotationWidth),
                    Is.EqualTo(Slice(motionCtx, 0, RpmManifest.RotationWidth)), $"prev_pred row {r}");
        }

        [Test]
        public void Begin_FirstTenFrames_SchedulesNoStep()
        {
            ModelLocalFiles.RequireRpm();
            var seq = ModelExport.Rpm.LoadSequence(kSequence);
            using var sut = NewRunner(Manifest());
            sut.StartStanding(seq.JointsRest);

            for (int t = 0; t < 10; t++)
            {
                Assert.That(sut.Begin(Slice(seq.SparseCtx, t * kInputWidth, kInputWidth)), Is.False, $"frame {t}");
                Assert.That(sut.IsStepPending, Is.False, $"pending after frame {t}");
            }
            bool eleventh = sut.Begin(Slice(seq.SparseCtx, 10 * kInputWidth, kInputWidth));
            sut.Complete();

            Assert.That(eleventh, Is.True, "frame 10 steps");
            Assert.That(sut.StepCount, Is.EqualTo(1), "steps");
            Assert.That(sut.LastCommittedFrame, Is.EqualTo(10), "committed frame");
        }

        [Test]
        public void Step_EvaluationSeed_CommitsTheNewestInputFrame()
        {
            ModelLocalFiles.RequireRpm();
            var manifest = Manifest();
            var seq = ModelExport.Rpm.LoadSequence(kSequence);
            using var sut = NewRunner(manifest);
            Start(sut, manifest, seq);

            float worst = 0f, worstShifted = float.MaxValue;
            for (int t = 0; t < 40; t++)
            {
                if (!sut.Step(Slice(seq.SparseCtx, t * kInputWidth, kInputWidth))) continue;
                Assert.That(sut.LastCommittedFrame, Is.EqualTo(t), "committed frame");
                worst = Mathf.Max(worst, MaxDiff(sut.MotionOut, seq.ReferencePrediction, t));
                worstShifted = Mathf.Min(worstShifted, MaxDiff(sut.MotionOut, seq.ReferencePrediction, t - 1));
            }

            Assert.That(sut.StepCount, Is.EqualTo(30), "steps on frames 10 … 39");
            Assert.That(worst, Is.LessThan(1e-4f), "vs ref_pred_motion of the same frame");
            Assert.That(worstShifted, Is.GreaterThan(1e-3f), "vs the frame before: the alignment is not ambiguous");
        }

        // A coroutine: the readback only completes as editor frames pass.
        [UnityTest]
        public IEnumerator TryComplete_AfterTheGpuFinishes_CollectsTheReferenceRow()
        {
            ModelLocalFiles.RequireRpm();
            var manifest = Manifest();
            var seq = ModelExport.Rpm.LoadSequence(kSequence);
            using var sut = NewRunner(manifest);
            Start(sut, manifest, seq);
            for (int t = 0; t < 10; t++) sut.Begin(Slice(seq.SparseCtx, t * kInputWidth, kInputWidth));
            sut.Begin(Slice(seq.SparseCtx, 10 * kInputWidth, kInputWidth));

            bool collected = false;
            for (int frame = 0; frame < 300 && !collected; frame++)
            {
                yield return null;
                collected = sut.TryComplete();
            }

            Assert.That(collected, Is.True, "collected within 300 editor frames");
            Assert.That(sut.MotionOut, Is.EqualTo(Slice(seq.ReferencePrediction, 10 * kMotionWidth, kMotionWidth)).Within(1e-4f));
        }

        [Test]
        public void Constructor_ManifestWindowNotTheGraphs_ThrowsInvalidDataException()
        {
            ModelLocalFiles.RequireRpm();
            var json = ManifestJson().Replace("\"prediction_window\": 5", "\"prediction_window\": 10");

            Assert.That(() => NewRunner(RpmManifest.Parse(json)).Dispose(),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("prev_pred"));
        }

        [Test]
        public void Begin_BeforeStart_ThrowsInvalidOperationException()
        {
            ModelLocalFiles.RequireRpm();
            using var sut = NewRunner(Manifest());

            Assert.That(() => sut.Begin(new float[kInputWidth]), Throws.TypeOf<InvalidOperationException>());
        }

        static void Start(RpmStepRunner runner, RpmManifest manifest, GorpSequence seq)
        {
            var motionCtx = new float[manifest.StateFrames * RpmManifest.RotationWidth];
            var prevPred = new float[manifest.PredictionWindow * RpmManifest.RotationWidth];
            RpmStepRunner.EvaluationState(seq.GroundTruth, manifest, motionCtx, prevPred);
            runner.Start(motionCtx, prevPred, seq.JointsRest);
        }

        // Max |motion - reference row f| over the 198 floats.
        static float MaxDiff(float[] motion, float[] reference, int f)
        {
            float worst = 0f;
            for (int k = 0; k < kMotionWidth; k++)
                worst = Mathf.Max(worst, Mathf.Abs(motion[k] - reference[f * kMotionWidth + k]));
            return worst;
        }
    }
}
