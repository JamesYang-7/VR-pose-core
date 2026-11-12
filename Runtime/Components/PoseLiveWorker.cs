using MyUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using VrChat;

/// <summary>
/// The live pipeline shared by the pose models (e.g. <see cref="RpmLiveWorker"/>), the <see cref="LiveWorker"/> that
/// runs a network: head + two controller poses in, the 22-joint SMPL-X avatar out. A subclass supplies the model's
/// step loop (<see cref="IPoseStepRunner"/>), its input rule and its frame rate.
///
/// <para>The model runs at its own rate (30 fps), whatever the display rate. On that clock this samples the
/// tracked poses (interpolated to the tick between the last two rendered frames), converts them to GORP's
/// raw device format (<see cref="GorpDevice.FromUnity"/>; the controllers get the grip offsets and corrections first, <see cref="GorpDevice.FromGrip"/>),
/// builds the 54-float input frame (<see cref="GorpInputProcessor"/>) and schedules one step
/// (<see cref="IPoseStepRunner.Begin"/>). The result is collected on a later frame without blocking, so the
/// main thread pays only for the scheduling, not for the GPU.</para>
///
/// <para>Every rendered frame the avatar shows the model's pose. With <see cref="SmoothPose"/> on, the pose is
/// interpolated on the model's own clock (<see cref="PoseTimeline"/>): each result at the time of the tick its input
/// was sampled at, shown one model frame (33 ms) plus the step's latency later, so the body moves at a steady speed
/// whenever results arrive. Off shows each result as it arrives. The body is then moved so its eyes land on the
/// headset (<see cref="EyeAnchor"/>), and the wrists can be IK-pinned to the live wrist estimates, so head and hands
/// follow the tracking at the display rate.</para>
///
/// <para>The rest skeleton the model needs (<c>joints_rest</c>) is read from the avatar itself
/// (<see cref="GorpPose.RestJoints"/>), and the rollout starts from the model's standing seed. The tracking origin
/// must be the floor: the models read absolute height.</para>
/// </summary>
public abstract class PoseLiveWorker : LiveWorker
{
    /// <summary>Start the rollout immediately instead of waiting for the trigger (handy for replay).</summary>
    [field: SerializeField]
    [field: Tooltip("Start immediately instead of waiting for the right-trigger / start key.")]
    public bool AutoStart { get; set; }

    /// <summary>Keyboard key (desktop / replay) that starts the pipeline; the VR right-trigger also starts it.</summary>
    [field: SerializeField]
    [field: Tooltip("Keyboard key that starts the pipeline (VR right-trigger also starts it).")]
    public Key StartKey { get; set; } = Key.Space;

    /// <summary>Pin the avatar's wrists onto the live wrist estimates each frame via arm IK.</summary>
    [field: SerializeField]
    [field: Tooltip("Pin the wrists each frame (arm IK) to where the controllers put them (controller minus GORP's controller-to-wrist offset), at the display rate.")]
    public bool PinWristsToControllers { get; set; } = true;

    /// <summary>Interpolate between the model's last two poses at the display rate.</summary>
    [field: SerializeField]
    [field: Tooltip("Interpolate the model's poses on its own clock every rendered frame: a steady speed at any display rate, one model frame (33 ms) plus the step's latency later. Off: each pose shows as it arrives (30 Hz steps).")]
    public bool SmoothPose { get; set; } = true;

    /// <inheritdoc/>
    public override bool HasPose => _hasPose;

    /// <inheritdoc/>
    public override bool PinsWrists => PinWristsToControllers;

    /// <summary>Model frames committed so far (0 before the rollout starts).</summary>
    public int CommittedFrames => _runner?.StepCount ?? 0;

    /// <summary>Main-thread time of the last model step in ms (<see cref="IPoseStepRunner.LastMainThreadMs"/>).</summary>
    public double LastInferenceMs => _runner?.LastMainThreadMs ?? 0;

    /// <summary>Time from scheduling the last step to its result in ms (<see cref="IPoseStepRunner.LastLatencyMs"/>).</summary>
    public double LastLatencyMs => _runner?.LastLatencyMs ?? 0;

    /// <summary>The model's step loop, or null after logging what is missing.</summary>
    protected abstract IPoseStepRunner CreateRunner();

    /// <summary>The model's input rule; called after <see cref="CreateRunner"/> succeeded.</summary>
    protected abstract GorpInputProcessor CreateInputProcessor();

    /// <summary>The model's frame rate, which the tick runs at; read after <see cref="CreateRunner"/> succeeded.</summary>
    protected virtual float ModelFps => GorpSequence.Fps;

    const int kHead = 15, kLeftWrist = 20, kRightWrist = 21;

    bool _running, _seeded;
    float _tick;
    Transform[] _bones;
    Transform _rootBone;
    EyeAnchor _eyes;
    float[] _jointsRest;
    IPoseStepRunner _runner;
    GorpInputProcessor _input;
    readonly float[] _raw = new float[GorpDevice.Points * GorpDevice.RawWidth];
    readonly float[] _frame = new float[PoseLayout.InputWidth];

    // Device poses of the previous rendered frame, for resampling to the model's tick.
    readonly Pose[] _lastPoses = new Pose[GorpDevice.Points];
    readonly Pose[] _tickPoses = new Pose[GorpDevice.Points];
    bool _hasLastPoses;
    float _sinceTick;

    // The model's results on its clock (rig-local rotations, pelvis in Unity world), the game time of each scheduled
    // step's tick until its result arrives, and the pose shown this frame.
    PoseTimeline _timeline;
    readonly System.Collections.Generic.Queue<float> _pendingTicks = new();
    Quaternion[] _result, _shownPose;
    Vector3 _shownPelvis;
    bool _hasPose;

    void Start()
    {
        string tag = $"[{GetType().Name}]";
        if (AvatarRoot == null || Head == null || LeftController == null || RightController == null)
        {
            Debug.LogError($"{tag} Missing references (Head/LeftController/RightController/AvatarRoot). Disabling.", this);
            enabled = false;
            return;
        }

        _bones = new Transform[AnimUtils.boneNum];
        AnimUtils.BindBones(AvatarRoot, ref _bones);
        _rootBone = _bones[0].parent; // the rig's root-motion node (pelvis.parent)

        var leftEye = AnimUtils.FindChildRecursive(AvatarRoot, "left_eye_smplhf");
        var rightEye = AnimUtils.FindChildRecursive(AvatarRoot, "right_eye_smplhf");
        if (leftEye == null || rightEye == null)
        {
            Debug.LogError($"{tag} Avatar has no left_eye_smplhf/right_eye_smplhf bones. Disabling.", this);
            enabled = false;
            return;
        }
        _eyes = new EyeAnchor(_bones[kHead], leftEye, rightEye);
        _jointsRest = GorpPose.RestJoints(_bones);

        _runner = CreateRunner();
        if (_runner == null)
        {
            enabled = false;
            return;
        }
        _input = CreateInputProcessor();
        _tick = 1f / ModelFps;
        _timeline = new PoseTimeline(PoseLayout.Joints, _tick);
        _result = new Quaternion[PoseLayout.Joints];
        _shownPose = new Quaternion[PoseLayout.Joints];
    }

    void OnDestroy() => _runner?.Dispose();

    // LateUpdate: the TrackedPoseDrivers / replay sources have written the rig transforms this frame.
    void LateUpdate()
    {
        if (_runner.TryComplete()) TakeResult();

        if (!_running && (AutoStart || StartPressed())) _running = true;
        AdvanceClock();

        if (_hasPose) ShowPose();
    }

    // One input frame per model tick: the poses at the tick (between the last two rendered frames), then a step.
    void AdvanceClock()
    {
        var head = new Pose(Head.position, Head.rotation);
        var left = GorpControllerPose(LeftController, GorpDevice.Left);
        var right = GorpControllerPose(RightController, GorpDevice.Right);
        if (!_hasLastPoses)
        {
            _lastPoses[0] = head; _lastPoses[1] = left; _lastPoses[2] = right;
            _hasLastPoses = true;
            _sinceTick = _tick; // tick on this frame
        }

        float dt = Time.deltaTime;
        float before = _sinceTick;
        _sinceTick += dt;
        if (_sinceTick >= _tick)
        {
            // Fraction of this frame at which the tick fell. After a hitch the backlog is dropped, not replayed:
            // replaying identical samples would only feed the model zero velocities.
            float t = dt > 0f ? Mathf.Clamp01((_tick - before) / dt) : 1f;
            _sinceTick = Mathf.Repeat(_sinceTick - _tick, _tick);
            _tickPoses[0] = Lerp(_lastPoses[0], head, t);
            _tickPoses[1] = Lerp(_lastPoses[1], left, t);
            _tickPoses[2] = Lerp(_lastPoses[2], right, t);
            Tick(Time.time - (1f - t) * dt);
        }
        _lastPoses[0] = head; _lastPoses[1] = left; _lastPoses[2] = right;
    }

    // tickTime: the game time the input frame was sampled at, between the last rendered frame and this one.
    void Tick(float tickTime)
    {
        for (int d = 0; d < GorpDevice.Points; d++)
            GorpDevice.FromUnity(_tickPoses[d].position, _tickPoses[d].rotation, 1f, _raw, d * GorpDevice.RawWidth);
        // The processor runs before the start too, so the first model frame has real velocities.
        if (!_input.Push(_raw, 0, _frame) || !_running) return;

        if (!_seeded)
        {
            _runner.StartStanding(_jointsRest);
            _seeded = true;
        }
        else if (_runner.IsStepPending)
        {
            // The GPU took longer than a model frame; wait for it rather than drop a frame of history.
            _runner.Complete();
            TakeResult();
        }
        // False while the model is still filling its input window (RPM: 10 frames): no result will come for this tick.
        if (_runner.Begin(_frame)) _pendingTicks.Enqueue(tickTime);
    }

    // The newest model pose goes on the timeline at its tick's time. Steps complete in the order they were scheduled.
    void TakeResult()
    {
        var tickTime = _pendingTicks.Count > 0 ? _pendingTicks.Dequeue() : Time.time;
        GorpPose.RotationsFromMatrices(_runner.JointRotations, _result);
        _timeline.Add(tickTime, Time.time, _result, GorpPose.ToUnity(GorpSequence.JointPosition(_runner.MotionOut, 0, 0)));
        _hasPose = true;
    }

    // Pose the avatar, put its eyes on the headset, then pin the wrists.
    void ShowPose()
    {
        if (SmoothPose) _timeline.Sample(Time.time, _shownPose, out _shownPelvis);
        else _timeline.Newest(_shownPose, out _shownPelvis);
        GorpPose.Apply(_bones, _shownPose, _shownPelvis);
        _rootBone.position += Head.position - _eyes.EyesPosition;

        if (PinWristsToControllers)
        {
            AnimUtils.SolveTwoBoneIK(_bones[16], _bones[18], _bones[kLeftWrist], WristEstimate(LeftController, GorpDevice.Left));
            AnimUtils.SolveTwoBoneIK(_bones[17], _bones[19], _bones[kRightWrist], WristEstimate(RightController, GorpDevice.Right));
        }
    }

    static Pose Lerp(Pose a, Pose b, float t) =>
        new(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));

    bool StartPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[StartKey].wasPressedThisFrame) return true;
        var rh = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        return rh.isValid
            && rh.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed)
            && pressed;
    }
}
