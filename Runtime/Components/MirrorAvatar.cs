using MyUtils;
using UnityEngine;

/// <summary>
/// Makes a second avatar copy the driven avatar's pose each frame, so it can stand in
/// front of the user facing them — a "mirror" to watch the reconstructed pose from the
/// front in the Game view (which otherwise rides the head and can't see the body).
///
/// Copies the 22 SMPL bone local rotations (not the root head-align translation, so the
/// mirror stays planted where it's placed). The mirror's facing comes from its own root
/// GameObject transform (rotate it 180° to face the user). This is a pose-copy twin, not
/// a true left-right reflection — good enough to judge pose plausibility; a real mirror
/// flip can be added later if wanted.
///
/// Copies in <see cref="Application.onBeforeRender"/> so it runs after the source has
/// been posed in its LateUpdate, before the skinned meshes render.
/// </summary>
public sealed class MirrorAvatar : MonoBehaviour
{
    /// <summary>The driven avatar whose pose is copied.</summary>
    [field: SerializeField] public Transform SourceRoot { get; set; }

    /// <summary>The mirror avatar that receives the pose.</summary>
    [field: SerializeField] public Transform MirrorRoot { get; set; }

    Transform[] _src;
    Transform[] _dst;

    void Start()
    {
        if (SourceRoot == null || MirrorRoot == null) { Debug.LogError("[MirrorAvatar] roots unset."); enabled = false; return; }
        _src = new Transform[AnimUtils.boneNum];
        _dst = new Transform[AnimUtils.boneNum];
        AnimUtils.BindBones(SourceRoot, ref _src);
        AnimUtils.BindBones(MirrorRoot, ref _dst);
    }

    void OnEnable() => Application.onBeforeRender += Copy;
    void OnDisable() => Application.onBeforeRender -= Copy;

    void Copy()
    {
        for (int i = 0; i < AnimUtils.boneNum; i++)
            _dst[i].localRotation = _src[i].localRotation;
    }
}
