using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VrChat.Testing
{
    [TestFixture]
    public class PoseTimelineTests
    {
        // A 30 Hz model on a 72 Hz display, as on a Quest: its results come 2 or 3 frames apart.
        const float Tick = 1f / 30f;
        const float Frame = 1f / 72f;
        // The pose turns steadily about +Y at this speed (degrees per second of tick time).
        const float Speed = 20f;

        static Quaternion PoseAt(float time) => Quaternion.AngleAxis(Speed * time, Vector3.up);

        static float Yaw(Quaternion q) => Quaternion.Angle(Quaternion.identity, q);

        // Runs whole display frames from time 0, as PoseLiveWorker does: the results of steps scheduled on the last
        // frame arrive, the tick that fell since then is scheduled, then the pose is sampled. Each step therefore
        // arrives on the frame after its tick's, as the asynchronous readback does. Returns each frame's time and yaw.
        static List<(float now, float yaw)> Run(PoseTimeline sut, float seconds, float stopResultsAt = float.MaxValue) =>
            Run(sut, seconds, stopResultsAt, out _);

        // As above; lastTick is the time of the last tick whose result was added.
        static List<(float now, float yaw)> Run(PoseTimeline sut, float seconds, float stopResultsAt, out float lastTick)
        {
            var result = new Quaternion[1];
            var shown = new Quaternion[1];
            var pending = new Queue<float>();
            var frames = new List<(float, float)>();
            var nextTick = 0f;
            lastTick = float.NaN;
            for (var f = 0; f * Frame <= seconds; f++)
            {
                var now = f * Frame;
                while (pending.Count > 0)
                {
                    var tickTime = pending.Dequeue();
                    result[0] = PoseAt(tickTime);
                    sut.Add(tickTime, now, result, Vector3.right * tickTime);
                    lastTick = tickTime;
                }
                while (nextTick <= now)
                {
                    if (nextTick < stopResultsAt) pending.Enqueue(nextTick);
                    nextTick += Tick;
                }
                if (sut.Sample(now, shown, out _)) frames.Add((now, Yaw(shown[0])));
            }
            return frames;
        }

        [Test]
        public void Sample_ResultsTwoOrThreeFramesApart_PoseTurnsAtASteadySpeed()
        {
            var sut = new PoseTimeline(joints: 1, Tick);

            var frames = Run(sut, seconds: 4f);

            // Degrees per second over each frame once the display clock has settled. Blending over one model frame
            // from each arrival dropped to ~40% of the speed on every 3-frame gap.
            var settled = frames.Where(f => f.now > 1.5f).ToList();
            var actual = settled.Skip(1).Select((f, i) => (f.yaw - settled[i].yaw) / Frame).ToArray();
            Assert.That(actual, Has.All.InRange(Speed * 0.98f, Speed * 1.02f));
        }

        [Test]
        public void Sample_Settled_ShowsThePoseOneModelFramePlusTheLatencyAgo()
        {
            var sut = new PoseTimeline(joints: 1, Tick);

            var (now, yaw) = Run(sut, seconds: 4f).Last();

            // Degrees: 0.06 is 3 ms of the turn.
            Assert.That(yaw, Is.EqualTo(Speed * (now - sut.Delay)).Within(0.06f));
        }

        [Test]
        public void Sample_ResultsStopComing_HoldsTheNewestPose()
        {
            var sut = new PoseTimeline(joints: 1, Tick);

            var frames = Run(sut, seconds: 3f, stopResultsAt: 2f, out var lastTick);

            Assert.That(frames.Last().yaw, Is.EqualTo(Speed * lastTick).Within(1e-3f));
        }

        // Adds results a tick apart, each arriving `latency` after its tick.
        static void AddResults(PoseTimeline sut, int count, float latency, ref float tickTime)
        {
            for (var i = 0; i < count; i++, tickTime += Tick)
                sut.Add(tickTime, tickTime + latency, new[] { PoseAt(tickTime) }, Vector3.zero);
        }

        [Test]
        public void Add_OneSlowResultAmongMany_KeepsTheLatency()
        {
            var sut = new PoseTimeline(joints: 1, Tick);
            var tickTime = 0f;
            AddResults(sut, 60, latency: 0.02f, ref tickTime);

            AddResults(sut, 1, latency: 0.08f, ref tickTime);

            Assert.That(sut.Latency, Is.EqualTo(0.02f).Within(1e-5f));
        }

        [Test]
        public void Add_SlowResultsForAWhile_RaisesTheLatency()
        {
            var sut = new PoseTimeline(joints: 1, Tick);
            var tickTime = 0f;
            AddResults(sut, 60, latency: 0.02f, ref tickTime);

            AddResults(sut, 10, latency: 0.05f, ref tickTime);

            Assert.That(sut.Latency, Is.EqualTo(0.05f).Within(1e-5f));
        }

        [Test]
        public void Sample_NoResultYet_ReturnsFalse()
        {
            var sut = new PoseTimeline(joints: 1, Tick);

            var actual = sut.Sample(0.5f, new Quaternion[1], out _);

            Assert.That(actual, Is.False);
        }

        [Test]
        public void Sample_OneResult_ShowsIt()
        {
            var sut = new PoseTimeline(joints: 1, Tick);
            sut.Add(tickTime: 1f, now: 1.02f, new[] { PoseAt(1f) }, Vector3.one);
            var shown = new Quaternion[1];

            sut.Sample(1.02f, shown, out _);

            Assert.That(Quaternion.Angle(shown[0], PoseAt(1f)), Is.LessThan(1e-3f));
        }
    }
}
