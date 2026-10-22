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
