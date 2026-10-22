using System;
using System.IO;
using MyUtils;
using UnityEditor;
using UnityEngine;
using VrChat;

/// <summary>
/// Bakes the RPM export's reference sequences (<see cref="ModelExport.Rpm"/>: the captures in
/// <c>Models/rpm/data/gorp/</c> with its default version's <c>output_ref/</c>) into
/// AnimationClips on the SMPL-X rig, one pair per sequence in <c>Assets/Generated/</c>:
/// <c>gorp_&lt;seq&gt;_gt</c> (mocap ground truth) and <c>gorp_&lt;seq&gt;_ref</c> (the model's own rollout,
/// <c>ref_pred_motion</c>), 30 fps, 22 local rotations plus the root bone's position, recentred on the first
/// ground-truth pelvis (<see cref="GorpSequence.RecenterOffset"/>). Any instance of
/// <c>smplx-visemes-unity.fbx</c> can play them, e.g. under an Animator for scrubbing in the Animation window.
///
/// <para>The <c>_ref</c> clips are also chained into <c>gorp_ref_cycle.controller</c>, which
/// <c>RetargetTest</c> plays.</para>
///
/// <para>Works on a hidden temporary rig, so no scene needs to be open. A live model rollout is not baked
/// here: it has to run in order, step by step.</para>
/// </summary>
public static class GorpClipBaker
{
    const string kFbxPath = "Assets/SMPLX/smplx-visemes-unity.fbx";
    const string kOutDir = "Assets/Generated";
    const string kRefCyclePath = kOutDir + "/gorp_ref_cycle.controller";

    [MenuItem("Tools/GORP/Bake Sequence Clips (ground truth + reference)")]
    static void BakeAll()
    {
        var names = ModelExport.Rpm.SequenceNames();
        if (names.Length == 0) { Debug.LogError($"[GORP] No sequences in {ModelExport.Rpm.DataDir}: extract the local-files zip."); return; }

        var rig = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(kFbxPath));
        rig.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var bones = new Transform[AnimUtils.boneNum];
            AnimUtils.BindBones(rig.transform, ref bones);
            foreach (var name in names)
            {
                var seq = ModelExport.Rpm.LoadSequence(name);
                var gt = Save(Bake(rig.transform, bones, seq.GroundTruth, seq.FrameCount, seq.RecenterOffset), $"gorp_{seq.Name}_gt");
                var pred = Save(Bake(rig.transform, bones, seq.ReferencePrediction, seq.FrameCount, seq.RecenterOffset), $"gorp_{seq.Name}_ref");
                Debug.Log($"[GORP] {seq.Name}: {seq.FrameCount} frames @ {GorpSequence.Fps} fps -> {gt}, {pred}");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig);
        }
        RebuildRefCycle();
    }

    // The model rollouts (gorp_<seq>_ref clips), in sequence order, as one loop.
    [MenuItem("Tools/GORP/Rebuild Reference Cycle Controller")]
    static void RebuildRefCycle()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), kOutDir);
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "gorp_*_ref.anim") : Array.Empty<string>();
        Array.Sort(files, StringComparer.Ordinal);
        var clips = Array.ConvertAll(files, f => AssetDatabase.LoadAssetAtPath<AnimationClip>($"{kOutDir}/{Path.GetFileName(f)}"));
        if (clips.Length == 0) { Debug.LogError("[GORP] No gorp_*_ref clips: bake them first."); return; }
        ClipCycle.Rebuild(kRefCyclePath, clips, "gorp_");
        Debug.Log($"[GORP] {kRefCyclePath}: {clips.Length} clips in a loop.");
    }

    static AnimationClip Bake(Transform rigRoot, Transform[] bones, float[] motion, int frames, Vector3 recenter)
    {
        var rootBone = bones[0].parent;
        var paths = new string[bones.Length];
        for (int i = 0; i < bones.Length; i++) paths[i] = AnimationUtility.CalculateTransformPath(bones[i], rigRoot);
        string rootPath = AnimationUtility.CalculateTransformPath(rootBone, rigRoot);

        var rot = new AnimationCurve[bones.Length, 4];
        for (int i = 0; i < bones.Length; i++)
            for (int c = 0; c < 4; c++) rot[i, c] = new AnimationCurve();
        var pos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

        var q = new Quaternion[bones.Length];
        for (int f = 0; f < frames; f++)
        {
            GorpPose.RotationsFromSixD(motion, f * PoseLayout.MotionWidth, q);
            GorpPose.Apply(bones, q, GorpPose.ToUnity(GorpSequence.JointPosition(motion, f, 0)) + recenter);
            float time = f / GorpSequence.Fps;
            for (int i = 0; i < bones.Length; i++)
            {
                var r = bones[i].localRotation; // bone 0 includes SetPose's up-axis fix
                rot[i, 0].AddKey(time, r.x); rot[i, 1].AddKey(time, r.y); rot[i, 2].AddKey(time, r.z); rot[i, 3].AddKey(time, r.w);
            }
            var p = rootBone.localPosition;
            pos[0].AddKey(time, p.x); pos[1].AddKey(time, p.y); pos[2].AddKey(time, p.z);
        }

        var clip = new AnimationClip { frameRate = GorpSequence.Fps };
        string[] rotProps = { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
        for (int i = 0; i < bones.Length; i++)
            for (int c = 0; c < 4; c++) SetCurve(clip, paths[i], rotProps[c], rot[i, c]);
        SetCurve(clip, rootPath, "localPosition.x", pos[0]);
        SetCurve(clip, rootPath, "localPosition.y", pos[1]);
        SetCurve(clip, rootPath, "localPosition.z", pos[2]);
        clip.EnsureQuaternionContinuity();
        return clip;
    }

    static void SetCurve(AnimationClip clip, string path, string prop, AnimationCurve curve)
    {
        var binding = new EditorCurveBinding { path = path, type = typeof(Transform), propertyName = prop };
        AnimationUtility.SetEditorCurve(clip, binding, curve);
    }

    static string Save(AnimationClip clip, string name)
    {
        if (!AssetDatabase.IsValidFolder(kOutDir)) AssetDatabase.CreateFolder("Assets", "Generated");
        string path = $"{kOutDir}/{name}.anim";
        AssetDatabase.CreateAsset(clip, path);
        AssetDatabase.SaveAssets();
        return path;
    }
}
