using MyUtils;
using UnityEngine;
using VrChat;
using VrChat.Ik;

/// <summary>
/// The <see cref="LiveWorker"/> that poses the avatar by IK alone (<see cref="IkBody"/>): no pose model, no model files,
/// no GPU work, nothing to warm up. A stand-in for a pose model where one can't run, such as a new device or a PC
/// without the local-files zip, with the same inputs and the same body out: standing, its eyes on the headset and
/// its wrists on the controllers.
///
/// <para>Every rendered frame, from the tracking as it is (no model clock, no smoothing, no delay): the eyes go on
/// <see cref="LiveWorker.Head"/> and the head turns as GORP's conversion turns the head joint for that headset pose;
/// each wrist goes where the conversion puts it for its controller (<see cref="LiveWorker.GorpControllerPose"/>,
/// <see cref="LiveWorker.WristEstimate"/>), turned likewise. So the same grip offsets and corrections as a pose
/// model's apply.</para>
///
/// <para>The first frame sets the pelvis height under the eyes, so start it with the wearer upright, as
/// <c>MirrorSession</c> does after calibration. The legs stay straight; nothing reacts to a controller losing
/// tracking (it holds its last pose).</para>
/// </summary>
public sealed class IkLiveWorker : LiveWorker
{
    /// <summary>How far (m) the eyes may move across the floor from over the pelvis before the body follows.</summary>
    [field: SerializeField]
    [field: Tooltip("How far (m) the head may move across the floor from over the pelvis before the body follows: within it a lean bends the spine, past it the body slides along (no steps).")]
    public float FollowRadius { get; set; } = 0.15f;

    /// <summary>How far (degrees) the head may turn from the body before the body turns with it.</summary>
    [field: SerializeField]
    [field: Range(0f, 90f)]
    [field: Tooltip("How far (degrees) the head may turn from the body before the body turns with it. Within it the spine takes part of the turn, most at the neck.")]
    public float TurnDeadZone { get; set; } = 40f;

    /// <inheritdoc/>
    public override bool HasPose => _hasPose;

    Transform[] _bones;
    IkBody _body;
    bool _hasPose;

    void Start()
    {
        if (AvatarRoot == null || Head == null || LeftController == null || RightController == null)
        {
            Debug.LogError("[IkLiveWorker] Missing references (Head/LeftController/RightController/AvatarRoot). Disabling.", this);
            enabled = false;
            return;
        }
        _bones = new Transform[AnimUtils.boneNum];
        AnimUtils.BindBones(AvatarRoot, ref _bones);
        var leftEye = AnimUtils.FindChildRecursive(AvatarRoot, "left_eye_smplhf");
        var rightEye = AnimUtils.FindChildRecursive(AvatarRoot, "right_eye_smplhf");
        if (leftEye == null || rightEye == null)
        {
            Debug.LogError("[IkLiveWorker] Avatar has no left_eye_smplhf/right_eye_smplhf bones. Disabling.", this);
            enabled = false;
            return;
        }
        // At rest the SMPL-X rig faces its root's forward.
        _body = new IkBody(_bones, new EyeAnchor(_bones[15], leftEye, rightEye), AvatarRoot.forward);
    }

    // LateUpdate: the TrackedPoseDrivers / replay sources have written the rig transforms this frame.
    void LateUpdate()
    {
        _body.FollowRadius = FollowRadius;
        _body.TurnDeadZone = TurnDeadZone;
        var rigRoot = _bones[0].parent.rotation;
        var head = SmplJoint(new Pose(Head.position, Head.rotation), GorpDevice.Head, rigRoot);
        var left = SmplJoint(GorpControllerPose(LeftController, GorpDevice.Left), GorpDevice.Left, rigRoot);
        var right = SmplJoint(GorpControllerPose(RightController, GorpDevice.Right), GorpDevice.Right, rigRoot);
        _body.Solve(Head.position, head.rotation, left, right);
        _hasPose = true;
    }
}
