using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MyUtils;
using UnityEditor;
using UnityEngine;
using VrChat;
using VrChat.Retarget;

/// <summary>
/// Measures how closely a retargeted character (the CC5 <c>Aaron</c>) follows the SMPL-X body, frame by
/// frame over the baked reference clips in <c>Assets/Generated</c>: <c>gorp_*_gt</c> / <c>gorp_*_ref</c>
/// (GORP mocap and RPM's rollout on it, 30 fps). It runs the real
/// <see cref="SmplRetarget"/> on hidden instances, so no scene needs to be open.
///
/// <para>Per frame it records the target − source error (metres, world axes) of the wrists, elbows, ankles
/// (horizontal) and foot soles (vertical). Errors are world errors with the bodies anchored as the
/// retargeter anchors them; the source's eyes are the headset, so they are errors as seen from the
/// headset. The sole is estimated from the ankle and toe joints: each sits a fixed height above its rig's
/// sole in the rest pose (measured on the meshes; Aaron's includes his shoes), so the sole is the lower of
/// the two estimates.</para>
///
/// <para>CC5 body sliders are simulated without an export: <c>Tools~/cc5/cc5_skeleton.py</c> writes the CC5
/// skeleton as the export's bind <c>localPosition</c>s (exact to 0.00 cm on the current export), so writing
/// them onto an Aaron instance gives the joints a re-export would have. Only joints: the mesh isn't
/// morphed. <see cref="WriteSliderResiduals"/> writes the residuals of each probed slider for
/// <c>Tools~/cc5/fit_sliders.py</c>.</para>
/// </summary>
public static class RetargetFit
{
    const string kSourceFbx = "Assets/SMPLX/smplx-visemes-unity.fbx";
    /// <summary>The character evaluated by default: Aaron re-exported with the fitted CC5 sliders.</summary>
    public const string TargetPrefab = "Assets/Characters/Aaron-SMPL/Prefabs/Aaron-SMPL.prefab";
    const string kClipsDir = "Assets/Generated";
    static readonly (string pattern, string group)[] kClipGroups =
    {
        ("gorp_*_gt.anim", "gorp_gt"), ("gorp_*_ref.anim", "gorp_ref"),
    };

    // Bones whose localPosition only places the body (CC5's root, and the hips over it — CC5 rests the
    // feet on the floor, the export doesn't); the anchor overrides both.
    static readonly HashSet<string> kPlacementBones = new() { "CC_Base_BoneRoot", "CC_Base_Hip" };

    /// <summary>Per-frame residual columns, target − source, metres.</summary>
    public static readonly string[] Columns =
    {
        "lwrist_x", "lwrist_y", "lwrist_z", "rwrist_x", "rwrist_y", "rwrist_z",
        "lsole_y", "rsole_y",
        "lelbow_x", "lelbow_y", "lelbow_z", "relbow_x", "relbow_y", "relbow_z",
        "lankle_x", "lankle_z", "rankle_x", "rankle_z",
        "eyes",
    };

    const int kLeftAnkle = 7, kRightAnkle = 8, kLeftToe = 10, kRightToe = 11, kHead = 15;
    const int kLeftElbow = 18, kRightElbow = 19, kLeftWrist = 20, kRightWrist = 21;

    static readonly HumanBodyBones[] kTargetJoints =
    {
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        HumanBodyBones.Head, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
    };

    /// <summary>How the target is retargeted for one evaluation.</summary>
    public readonly struct Setup
    {
        public readonly string Name;
        public readonly LimbAims Aims;
        public readonly bool AnchorEyes; // false: head joint on head joint (the pre-2026-10 anchor)
        public readonly Action<Animator> EditBind; // optional change to the target's bind before calibrating

        public Setup(string name, LimbAims aims, bool anchorEyes, Action<Animator> editBind = null)
        {
            Name = name;
            Aims = aims;
            AnchorEyes = anchorEyes;
            EditBind = editBind;
        }
    }

    [Serializable] class BoneVector { public string name; public float[] p; }
    [Serializable] class SliderWeight { public string name; public float weight; }
    [Serializable] class SliderDelta { public string slider; public BoneVector[] bones; }
    [Serializable] class SkeletonFile { public SliderWeight[] sliders; public BoneVector[] bones; }
    [Serializable] class ProbeFile { public float step; public SliderWeight[] sliders; public BoneVector[] bones; public SliderDelta[] delta; }

    static readonly Setup Default = new("eye anchor, arms + clavicles aimed", LimbAims.Arms | LimbAims.Clavicles, true);

    [MenuItem("Tools/Retarget/Evaluate Aaron on Reference Clips")]
    static void EvaluateMenu() => Debug.Log(EvaluateExport());

    /// <summary>The exported character (<see cref="TargetPrefab"/>) under the old and new retarget setups.</summary>
    public static string EvaluateExport() => EvaluateSetups(new[]
    {
        new Setup("head anchor, no aims (old)", LimbAims.None, false),
        new Setup("eye anchor, no aims", LimbAims.None, true),
        new Setup("eye anchor, arms aimed", LimbAims.Arms, true),
        new Setup("eye anchor, arms + clavicles aimed (default)", LimbAims.Arms | LimbAims.Clavicles, true),
    });

    /// <summary>
    /// Each setup on a character prefab (default <see cref="TargetPrefab"/>), or on a CC5 skeleton dump
    /// (<c>dump_skeleton</c>) applied to it if given.
    /// </summary>
    public static string EvaluateSetups(IEnumerable<Setup> setups, string skeletonJson = null, string targetPrefab = TargetPrefab)
    {
        var bones = skeletonJson != null ? ToDictionary(JsonUtility.FromJson<SkeletonFile>(File.ReadAllText(skeletonJson)).bones) : null;
        using var session = new Session(targetPrefab);
        var sb = new StringBuilder($"[RetargetFit] {session.Describe()}\n");
        foreach (var setup in setups) sb.Append(session.Summarize(setup.Name, session.Run(setup, bones)));
        return sb.ToString();
    }

    /// <summary>
    /// The default setup on a CC5 skeleton dump (<c>dump_skeleton</c>), next to the export, optionally writing
    /// the dump's per-frame residuals (frames × <see cref="Columns"/>) to <paramref name="residualsNpy"/>.
    /// </summary>
    public static string EvaluateSkeleton(string skeletonJson, string residualsNpy = null)
    {
        var file = JsonUtility.FromJson<SkeletonFile>(File.ReadAllText(skeletonJson));
        var setup = Default;
        using var session = new Session(TargetPrefab);
        var sliders = string.Join(", ", file.sliders.Where(s => Mathf.Abs(s.weight) > 1e-4f).Select(s => $"{s.name} {s.weight:+0.000;-0.000}"));
        var sb = new StringBuilder($"[RetargetFit] {session.Describe()}\n");
        sb.Append(session.Summarize("export", session.Run(setup, null)));
        var res = session.Run(setup, ToDictionary(file.bones));
        sb.Append(session.Summarize($"{Path.GetFileName(skeletonJson)} ({(sliders.Length > 0 ? sliders : "no sliders")})", res));
        if (residualsNpy != null) NpyWriter.SaveFloat32(residualsNpy, res, new[] { session.FrameCount, Columns.Length });
        return sb.ToString();
    }

    /// <summary>
    /// For a slider probe (<c>probe_sliders</c>): writes <c>residuals.npy</c> (1 + sliders, frames, columns) —
    /// row 0 the probe's base skeleton, row k the base plus <paramref name="step"/> of slider k — and
    /// <c>fit_meta.json</c> (slider names, step, columns, each frame's clip group) to <paramref name="outDir"/>.
    /// </summary>
    public static string WriteSliderResiduals(string probeJson, string outDir, float step = 0.25f)
    {
        var probe = JsonUtility.FromJson<ProbeFile>(File.ReadAllText(probeJson));
        var setup = Default;
        var baseBones = ToDictionary(probe.bones);
        using var session = new Session(TargetPrefab);

        int n = probe.delta.Length, frames = session.FrameCount, cols = Columns.Length;
        var all = new float[(1 + n) * frames * cols];
        Array.Copy(session.Run(setup, baseBones), all, frames * cols);
        for (int k = 0; k < n; k++)
        {
            var bones = new Dictionary<string, Vector3>(baseBones);
            foreach (var d in probe.delta[k].bones)
            {
                if (bones.TryGetValue(d.name, out var v)) bones[d.name] = v + step * new Vector3(d.p[0], d.p[1], d.p[2]);
            }
            Array.Copy(session.Run(setup, bones), 0, all, (1 + k) * frames * cols, frames * cols);
        }

        Directory.CreateDirectory(outDir);
        NpyWriter.SaveFloat32(Path.Combine(outDir, "residuals.npy"), all, new[] { 1 + n, frames, cols });
        var meta = new StringBuilder("{\n");
        meta.Append($" \"step\": {step.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n");
        meta.Append($" \"sliders\": [{string.Join(", ", probe.delta.Select(d => $"\"{d.slider}\""))}],\n");
        meta.Append($" \"columns\": [{string.Join(", ", Columns.Select(c => $"\"{c}\""))}],\n");
        meta.Append($" \"groups\": [{string.Join(", ", kClipGroups.Select(g => $"\"{g.group}\""))}],\n");
        meta.Append($" \"frame_group\": [{string.Join(",", session.FrameGroups)}]\n}}\n");
        File.WriteAllText(Path.Combine(outDir, "fit_meta.json"), meta.ToString());
        return $"[RetargetFit] {n} sliders x {frames} frames -> {outDir}\n" + session.Summarize("probe base", session.Run(setup, baseBones));
    }

    static Dictionary<string, Vector3> ToDictionary(BoneVector[] bones) =>
        bones.ToDictionary(b => b.name, b => new Vector3(b.p[0], b.p[1], b.p[2]));

    /// <summary>Hidden source and target instances plus the sampled clip frames.</summary>
    sealed class Session : IDisposable
    {
        readonly GameObject _sourceGo, _targetGo;
        readonly Transform[] _src = new Transform[AnimUtils.boneNum];
        readonly Transform _srcRootBone;
        readonly Animator _animator;
        readonly Transform[] _tgt;
        readonly Dictionary<string, Transform> _tgtByName = new();
        readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> _tgtBind = new();
        readonly List<Quaternion[]> _frameRotations = new();
        readonly List<Vector3> _frameRootPositions = new();
        readonly List<int> _frameGroups = new();
        readonly string[] _clipNames;
        // Height above the rig's sole at rest, per side: ankle, toe.
        readonly float _srcAnkleL, _srcAnkleR, _srcToeL, _srcToeR, _tgtAnkleL, _tgtAnkleR, _tgtToeL, _tgtToeR;

        public int FrameCount => _frameGroups.Count;
        public IReadOnlyList<int> FrameGroups => _frameGroups;

        public Session(string targetPrefab)
        {
            _sourceGo = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(kSourceFbx));
            _sourceGo.hideFlags = HideFlags.HideAndDontSave;
            _targetGo = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefab));
            _targetGo.hideFlags = HideFlags.HideAndDontSave;

            var src = _src;
            AnimUtils.BindBones(_sourceGo.transform, ref src);
            _srcRootBone = _src[0].parent;
            _animator = _targetGo.GetComponent<Animator>();
            _tgt = kTargetJoints.Select(b => _animator.GetBoneTransform(b)).ToArray();
            foreach (var t in _targetGo.GetComponentsInChildren<Transform>(true))
            {
                _tgtByName[t.name] = t;
                _tgtBind[t] = (t.localPosition, t.localRotation);
            }

            (_srcAnkleL, _srcAnkleR, _srcToeL, _srcToeR) = SourceRestHeights();
            (_tgtAnkleL, _tgtAnkleR, _tgtToeL, _tgtToeR) = RestHeights(_targetGo, _targetGo.transform.up,
                _tgt[0].position, _tgt[1].position, _tgt[2].position, _tgt[3].position);

            var names = new List<string>();
            for (int g = 0; g < kClipGroups.Length; g++)
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), kClipsDir);
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, kClipGroups[g].pattern) : Array.Empty<string>();
                Array.Sort(files, StringComparer.Ordinal);
                foreach (var f in files)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{kClipsDir}/{Path.GetFileName(f)}");
                    int frames = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
                    for (int i = 0; i < frames; i++)
                    {
                        clip.SampleAnimation(_sourceGo, i / clip.frameRate);
                        _frameRotations.Add(_src.Select(b => b.localRotation).ToArray());
                        _frameRootPositions.Add(_srcRootBone.localPosition);
                        _frameGroups.Add(g);
                    }
                    names.Add(clip.name);
                }
            }
            if (_frameGroups.Count == 0) throw new InvalidOperationException($"No reference clips in {kClipsDir}: bake them first (Tools/GORP).");
            _clipNames = names.ToArray();
        }

        public string Describe() =>
            $"{_clipNames.Length} clips, {FrameCount} frames ({string.Join(", ", kClipGroups.Select((g, i) => $"{g.group} {_frameGroups.Count(x => x == i)}"))}). " +
            $"Rest heights above the sole, ankle/toe cm: SMPL-X L {_srcAnkleL * 100:F1}/{_srcToeL * 100:F1} R {_srcAnkleR * 100:F1}/{_srcToeR * 100:F1}, " +
            $"{_targetGo.name.Replace("(Clone)", "")} L {_tgtAnkleL * 100:F1}/{_tgtToeL * 100:F1} R {_tgtAnkleR * 100:F1}/{_tgtToeR * 100:F1}";

        // SMPL-X lies Z-up at bind; its rest is the zero pose, up = the pelvis's up (as SmplRetarget calibrates).
        (float, float, float, float) SourceRestHeights()
        {
            var src = _src;
            var rest = new Quaternion[AnimUtils.boneNum];
            for (int i = 0; i < rest.Length; i++) rest[i] = Quaternion.identity;
            AnimUtils.SetPose(ref src, ref rest);
            return RestHeights(_sourceGo, _src[0].rotation * Vector3.up,
                _src[kLeftAnkle].position, _src[kRightAnkle].position, _src[kLeftToe].position, _src[kRightToe].position);
        }

        // Lowest skinned vertex along `up` on each side of the body (split halfway between the ankles).
        static (float, float, float, float) RestHeights(GameObject rig, Vector3 up, Vector3 ankleL, Vector3 ankleR, Vector3 toeL, Vector3 toeR)
        {
            var right = Vector3.ProjectOnPlane(ankleR - ankleL, up).normalized;
            var mid = (ankleL + ankleR) * 0.5f;
            float minL = float.MaxValue, minR = float.MaxValue;
            var mesh = new Mesh();
            foreach (var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.BakeMesh(mesh, true);
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    float h = Vector3.Dot(w, up);
                    if (Vector3.Dot(w - mid, right) < 0f) minL = Mathf.Min(minL, h);
                    else minR = Mathf.Min(minR, h);
                }
            }
            UnityEngine.Object.DestroyImmediate(mesh);
            return (Vector3.Dot(ankleL, up) - minL, Vector3.Dot(ankleR, up) - minR, Vector3.Dot(toeL, up) - minL, Vector3.Dot(toeR, up) - minR);
        }

        /// <summary>Residuals, frames × <see cref="Columns"/>, for the target with optional bind localPositions by bone name.</summary>
        public float[] Run(Setup setup, IReadOnlyDictionary<string, Vector3> bindPositions)
        {
            foreach (var (t, bind) in _tgtBind) t.SetLocalPositionAndRotation(bind.pos, bind.rot);
            if (bindPositions != null)
            {
                foreach (var (name, p) in bindPositions)
                {
                    if (!kPlacementBones.Contains(name) && _tgtByName.TryGetValue(name, out var t)) t.localPosition = p;
                }
            }

            setup.EditBind?.Invoke(_animator);
            var retarget = new SmplRetarget(_sourceGo.transform, _animator, setup.Aims) { Anchor = setup.AnchorEyes };
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            int cols = Columns.Length;
            var res = new float[FrameCount * cols];
            for (int f = 0; f < FrameCount; f++)
            {
                var rot = _frameRotations[f];
                for (int i = 0; i < rot.Length; i++) _src[i].localRotation = rot[i];
                _srcRootBone.localPosition = _frameRootPositions[f];
                retarget.Apply();
                if (!setup.AnchorEyes) hips.position += _src[kHead].position - _tgt[4].position;

                int o = f * cols;
                Put(res, o + 0, _tgt[7].position - _src[kLeftWrist].position);
                Put(res, o + 3, _tgt[8].position - _src[kRightWrist].position);
                res[o + 6] = Sole(_tgt[0], _tgt[2], _tgtAnkleL, _tgtToeL) - Sole(_src[kLeftAnkle], _src[kLeftToe], _srcAnkleL, _srcToeL);
                res[o + 7] = Sole(_tgt[1], _tgt[3], _tgtAnkleR, _tgtToeR) - Sole(_src[kRightAnkle], _src[kRightToe], _srcAnkleR, _srcToeR);
                Put(res, o + 8, _tgt[5].position - _src[kLeftElbow].position);
                Put(res, o + 11, _tgt[6].position - _src[kRightElbow].position);
                var al = _tgt[0].position - _src[kLeftAnkle].position;
                var ar = _tgt[1].position - _src[kRightAnkle].position;
                res[o + 14] = al.x; res[o + 15] = al.z; res[o + 16] = ar.x; res[o + 17] = ar.z;
                res[o + 18] = (retarget.TargetAnchorPosition - retarget.SourceAnchorPosition).magnitude;
            }
            return res;
        }

        static float Sole(Transform ankle, Transform toe, float ankleHeight, float toeHeight) =>
            Mathf.Min(ankle.position.y - ankleHeight, toe.position.y - toeHeight);

        static void Put(float[] a, int o, Vector3 v)
        {
            a[o] = v.x; a[o + 1] = v.y; a[o + 2] = v.z;
        }

        /// <summary>One line per clip group plus all frames: wrist, elbow, sole and ankle errors in cm.</summary>
        public string Summarize(string title, float[] res)
        {
            int cols = Columns.Length;
            var sb = new StringBuilder($"  {title}\n");
            for (int g = -1; g < kClipGroups.Length; g++)
            {
                var idx = Enumerable.Range(0, FrameCount).Where(f => g < 0 || _frameGroups[f] == g).ToArray();
                if (idx.Length == 0) continue;
                var wrist = idx.SelectMany(f => new[] { Norm(res, f * cols + 0), Norm(res, f * cols + 3) }).ToArray();
                var elbow = idx.SelectMany(f => new[] { Norm(res, f * cols + 8), Norm(res, f * cols + 11) }).ToArray();
                var sole = idx.SelectMany(f => new[] { res[f * cols + 6], res[f * cols + 7] }).ToArray();
                var ankle = idx.SelectMany(f => new[] { Hyp(res[f * cols + 14], res[f * cols + 15]), Hyp(res[f * cols + 16], res[f * cols + 17]) }).ToArray();
                float eyes = idx.Max(f => res[f * cols + 18]);
                sb.Append($"    {(g < 0 ? "all" : kClipGroups[g].group),-9} wrist {Cm(wrist.Average())} / p95 {Cm(P95(wrist))} | elbow {Cm(elbow.Average())} | " +
                          $"sole {Cm(sole.Average()),5} signed, {Cm(sole.Average(Mathf.Abs))} / p95 {Cm(P95(sole.Select(Mathf.Abs)))} abs, below by >2 cm {100f * sole.Count(s => s < -0.02f) / sole.Length:F0}% | " +
                          $"ankle horiz {Cm(ankle.Average())} | anchor gap max {Cm(eyes)}\n");
            }
            return sb.ToString();
        }

        static float Norm(float[] a, int o) => Mathf.Sqrt(a[o] * a[o] + a[o + 1] * a[o + 1] + a[o + 2] * a[o + 2]);
        static float Hyp(float x, float z) => Mathf.Sqrt(x * x + z * z);
        static string Cm(float m) => (m * 100f).ToString("F1");

        static float P95(IEnumerable<float> values)
        {
            var s = values.OrderBy(v => v).ToArray();
            return s[Mathf.Min(s.Length - 1, (int)(0.95f * s.Length))];
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(_sourceGo);
            UnityEngine.Object.DestroyImmediate(_targetGo);
        }
    }
}
