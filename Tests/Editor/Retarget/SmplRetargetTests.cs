using System.Collections.Generic;
using System.Linq;
using MyUtils;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrChat.Retarget;

namespace VrChat.Testing.Retarget
{
    /// <summary>
    /// <see cref="SmplRetarget"/> on the real rigs: the SMPL-X FBX, whose fingers come curled (the relaxed mean
    /// hand), onto the CC5 <c>Aaron-SMPL</c>. Both are git-ignored and come with the local-files zip; the tests are
    /// skipped without them.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public class SmplRetargetTests
    {
        const string SmplxFbx = "Assets/SMPLX/smplx-visemes-unity.fbx";
        const string AaronPrefab = "Assets/Characters/Aaron-SMPL/Prefabs/Aaron-SMPL.prefab";

        static readonly string[] s_smplxFingers = { "thumb", "index", "middle", "ring", "pinky" };

        GameObject _source;
        GameObject _target;

        [SetUp]
        public void SetUp()
        {
            var smplx = AssetDatabase.LoadAssetAtPath<GameObject>(SmplxFbx);
            var aaron = AssetDatabase.LoadAssetAtPath<GameObject>(AaronPrefab);
            Assume.That(smplx != null && aaron != null, $"{SmplxFbx} and {AaronPrefab} are git-ignored; they come with the local-files zip");
            _source = (GameObject)PrefabUtility.InstantiatePrefab(smplx);
            _target = (GameObject)PrefabUtility.InstantiatePrefab(aaron);
        }

        [TearDown]
        public void TearDown()
        {
            if (_source != null) Object.DestroyImmediate(_source);
            if (_target != null) Object.DestroyImmediate(_target);
        }

        // Per finger and side, the source's phalanx joints (1–3) and the target's Humanoid ones.
        IEnumerable<(Transform[] source, Transform[] target)> Fingers()
        {
            var animator = _target.GetComponentInChildren<Animator>();
            foreach (var (side, thumb) in new[] { ("left", HumanBodyBones.LeftThumbProximal), ("right", HumanBodyBones.RightThumbProximal) })
            {
                for (int f = 0; f < s_smplxFingers.Length; f++)
                {
                    var source = Enumerable.Range(1, 3).Select(k => AnimUtils.FindChildRecursive(_source.transform, $"{side}_{s_smplxFingers[f]}{k}")).ToArray();
                    var target = Enumerable.Range(0, 3).Select(k => animator.GetBoneTransform(thumb + f * 3 + k)).ToArray();
                    yield return (source, target);
                }
            }
        }

        [Test]
        public void Apply_SourceFingersCurled_TargetThumbAndMiddlePhalangesPointAlongTheSource()
        {
            var sut = new SmplRetarget(_source.transform, _target.GetComponentInChildren<Animator>());

            sut.Apply();

            // The thumb and middle finger (0 and 2 per hand) aren't turned for spacing.
            var actual = Fingers().Where((_, i) => i % 5 is 0 or 2).SelectMany(f => new[]
            {
                Vector3.Angle(f.source[1].position - f.source[0].position, f.target[1].position - f.target[0].position),
                Vector3.Angle(f.source[2].position - f.source[1].position, f.target[2].position - f.target[1].position),
            }).ToArray();
            Assert.That(actual, Has.Length.EqualTo(8));
            Assert.That(actual, Has.All.LessThan(1f));
        }

        [Test]
        public void Apply_SourceFingersCurled_TargetFingerGapsAreTheSourcesScaledByTheKnuckleSpan()
        {
            var sut = new SmplRetarget(_source.transform, _target.GetComponentInChildren<Animator>());

            sut.Apply();

            // Per hand: index, middle, ring, pinky (1–4); gaps between neighbouring last joints, in mm.
            var hands = Fingers().Select((f, i) => (f, i)).GroupBy(x => x.i / 5, x => x.f).Select(h => h.Skip(1).ToArray());
            var actual = new List<float>();
            foreach (var h in hands)
            {
                float scale = (h[3].target[0].position - h[0].target[0].position).magnitude / (h[3].source[0].position - h[0].source[0].position).magnitude;
                for (int f = 0; f < 3; f++)
                {
                    float sourceGap = (h[f + 1].source[2].position - h[f].source[2].position).magnitude;
                    float targetGap = (h[f + 1].target[2].position - h[f].target[2].position).magnitude;
                    actual.Add(Mathf.Abs(targetGap - scale * sourceGap) * 1000f);
                }
            }
            Assert.That(actual, Has.Count.EqualTo(6));
            Assert.That(actual, Has.All.LessThan(0.5f));
        }

        // Seats the source in its rest frame: thighs level and forward, shins straight down, upper arms 15° forward of
        // straight down and forearms pitched so the wrists sit 10 cm above the hip joints, on its lap.
        Transform[] SeatSourceWithHandsOnLap()
        {
            var bones = new Transform[AnimUtils.boneNum];
            AnimUtils.BindBones(_source.transform, ref bones);
            var rest = Enumerable.Repeat(Quaternion.identity, AnimUtils.boneNum).ToArray();
            AnimUtils.SetPose(ref bones, ref rest);
            var up = bones[0].rotation * Vector3.up;
            var lateral = (bones[1].position - bones[2].position).normalized;
            var forward = Vector3.Cross(up, lateral).normalized;
            if (Vector3.Dot(bones[10].position - bones[7].position, forward) < 0f) forward = -forward; // toes point forward

            void Aim(int joint, int child, Vector3 direction) =>
                bones[joint].rotation = Quaternion.FromToRotation(bones[child].position - bones[joint].position, direction) * bones[joint].rotation;

            Aim(1, 4, forward);
            Aim(2, 5, forward);
            Aim(4, 7, -up);
            Aim(5, 8, -up);
            var upperArm = Vector3.Slerp(-up, forward, 15f / 90f);
            var hips = (bones[1].position + bones[2].position) * 0.5f;
            float elbowHeight = Vector3.Dot(bones[16].position - hips, up) + (bones[18].position - bones[16].position).magnitude * Vector3.Dot(upperArm, up);
            float pitch = Mathf.Asin((elbowHeight - 0.1f) / (bones[20].position - bones[18].position).magnitude);
            var forearm = forward * Mathf.Cos(pitch) - up * Mathf.Sin(pitch);
            foreach (var (shoulder, elbow, wrist) in new[] { (16, 18, 20), (17, 19, 21) })
            {
                Aim(shoulder, elbow, upperArm);
                Aim(elbow, wrist, forearm);
            }
            return bones;
        }

        static Vector3 Midpoint(Transform a, Transform b) => (a.position + b.position) * 0.5f;

        [Test]
        public void Apply_AnchorAtHips_PutsTheTargetHipJointsOnTheSources()
        {
            var source = SeatSourceWithHandsOnLap();
            var animator = _target.GetComponentInChildren<Animator>();
            var sut = new SmplRetarget(_source.transform, animator) { AnchorAt = RetargetAnchor.Hips };

            sut.Apply();

            var actual = Midpoint(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));
            Assert.That((actual - Midpoint(source[1], source[2])).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void Apply_PinWristsToBodyScaledGoals_TargetWristsLandOnTheSourceWristsMovedOntoTheTargetTorso()
        {
            var source = SeatSourceWithHandsOnLap();
            var animator = _target.GetComponentInChildren<Animator>();
            var sut = new SmplRetarget(_source.transform, animator) { AnchorAt = RetargetAnchor.Hips, PinWrists = true, WristGoal = WristGoal.BodyScaled };

            sut.Apply();

            var sourceHips = Midpoint(source[1], source[2]);
            var sourceEyes = Midpoint(AnimUtils.FindChildRecursive(_source.transform, "left_eye_smplhf"), AnimUtils.FindChildRecursive(_source.transform, "right_eye_smplhf"));
            var targetHips = Midpoint(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightUpperLeg));
            var targetEyes = Midpoint(animator.GetBoneTransform(HumanBodyBones.LeftEye), animator.GetBoneTransform(HumanBodyBones.RightEye));
            // Metres from each target wrist to its goal; the arm IK is a few rounds of cyclic coordinate descent.
            var actual = new[] { (HumanBodyBones.LeftHand, 20), (HumanBodyBones.RightHand, 21) }.Select(w =>
                (animator.GetBoneTransform(w.Item1).position - TorsoMap.Point(source[w.Item2].position, sourceHips, sourceEyes, targetHips, targetEyes)).magnitude).ToArray();
            Assert.That(actual, Has.All.LessThan(0.01f));
        }

        [Test]
        public void Ctor_SourceFingersCurled_LeavesTheSourceFingersAsTheyWere()
        {
            var fingers = Fingers().SelectMany(f => f.source).ToArray();
            var before = fingers.Select(t => t.localRotation).ToArray();

            _ = new SmplRetarget(_source.transform, _target.GetComponentInChildren<Animator>());

            // Degrees, not exact equality: a Transform hands back a set rotation with last-bit rounding.
            var actual = fingers.Select((t, i) => Quaternion.Angle(t.localRotation, before[i])).ToArray();
            Assert.That(actual, Has.All.LessThan(0.01f));
        }
    }
}
