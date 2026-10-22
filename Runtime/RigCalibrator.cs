using System;
using MyUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VrChat
{
    /// <summary>
    /// Fits the XR rig to the scene once, automatically, at the start: turns the wearer to face
    /// <see cref="FaceTarget"/> and matches their height to the character's. Only the rig (<see cref="Origin"/>)
    /// moves, turning about the head and sliding straight up or down, so the wearer stays where they stand.
    ///
    /// <para><b>When.</b> As soon as the wearer stands at rest (<see cref="RigCalibration.IsAtRest"/>: head
    /// level, controllers in lowered hands) and still for <see cref="HoldSeconds"/>. The height is only right
    /// if they stand up straight then. Holding <see cref="RecalibrateInput"/> (default: X, the left controller's
    /// primary button) for <see cref="HoldSeconds"/> with the head still does it again at any time (release to
    /// re-arm). It replaced a held T-pose, which was awkward and could fire on its own when the arms spread wide in
    /// normal movement; the trigger and grip are left for pointing and holding.</para>
    ///
    /// <para><b>Height.</b> The rig moves so the headset sits <see cref="TargetHeight"/> above the scene floor:
    /// the height at which the pose model reconstructs <see cref="Character"/> standing in its T-pose
    /// (<see cref="RigCalibration.TPoseHeadsetHeight"/>), less <see cref="Margin"/>. Why: the runtime's floor
    /// can be off (a Quest over Link put a standing wearer's headset 2.11 m up), and a wearer taller than the
    /// character lifts its feet off the floor, because the model puts the head where the headset is and the legs
    /// can only straighten so far. Set a little lower, the body bends its knees slightly instead of
    /// floating.</para>
    /// </summary>
    public sealed class RigCalibrator : MonoBehaviour
    {
        /// <summary>The rig root that moves (the XR Origin).</summary>
        [field: SerializeField]
        [field: Tooltip("The rig root that turns and moves up or down (the XR Origin). Its level at Start is the scene floor.")]
        public Transform Origin { get; set; }

        /// <summary>The headset (the XR camera).</summary>
        [field: SerializeField]
        [field: Tooltip("The headset (the XR camera).")]
        public Transform Head { get; set; }

        /// <summary>The left controller.</summary>
        [field: SerializeField]
        [field: Tooltip("The left controller.")]
        public Transform LeftHand { get; set; }

        /// <summary>The right controller.</summary>
        [field: SerializeField]
        [field: Tooltip("The right controller.")]
        public Transform RightHand { get; set; }

        /// <summary>Root of the character the pose model drives (contains the named SMPL bones and its skinned mesh).</summary>
        [field: SerializeField]
        [field: Tooltip("Root of the character the pose model drives (the SMPL-X avatar). Its T-pose sets the target height.")]
        public Transform Character { get; set; }

        /// <summary>What the wearer is turned to face when calibrating. Optional.</summary>
        [field: SerializeField]
        [field: Tooltip("What the wearer is turned to face when calibrating. Optional: none keeps the heading.")]
        public Transform FaceTarget { get; set; }

        /// <summary>How far below the character's T-pose headset height to calibrate (m).</summary>
        [field: SerializeField]
        [field: Range(0f, 0.1f)]
        [field: Tooltip("Calibrate this far (m) below the character's T-pose headset height, so the body bends its knees slightly rather than floats.")]
        public float Margin { get; set; } = 0.02f;

        /// <summary>How long the wearer must hold still (s), at rest at the start or holding <see cref="RecalibrateInput"/>.</summary>
        [field: SerializeField]
        [field: Tooltip("Seconds the wearer must hold still, at rest at the start or holding the recalibrate button, before calibrating.")]
        public float HoldSeconds { get; set; } = 1f;

        /// <summary>The button that recalibrates when held for <see cref="HoldSeconds"/> with the head still.</summary>
        [field: SerializeField]
        [field: Tooltip("Held for HoldSeconds with the head still, recalibrates at any time (release to re-arm). Default: X, the left controller's primary button.")]
        public InputActionProperty RecalibrateInput { get; set; } =
            new(new InputAction("Recalibrate", InputActionType.Button, "<XRController>{LeftHand}/primaryButton"));

        /// <summary>Headset height calibration aims for, in metres above the scene floor (set at Start).</summary>
        public float TargetHeight { get; private set; }

        /// <summary>True once the rig has been calibrated.</summary>
        public bool IsCalibrated { get; private set; }

        /// <summary>Raised after every calibration, the first one and each recalibration, once the rig has moved.</summary>
        public event Action Calibrated;

        /// <summary>Headset height above the scene floor, in metres.</summary>
        public float HeadHeight => Head.position.y - _floorY;

        /// <summary>How far calibration has moved the rig, in metres (negative: down).</summary>
        public float Shift => Origin.position.y - _floorY;

        /// <summary>How far calibration has turned the rig, in degrees (positive: clockwise seen from above).</summary>
        public float Turn => Mathf.DeltaAngle(_startYaw, Origin.eulerAngles.y);

        /// <summary>Share of the current hold done, 0–1: at rest or on the button before the first calibration, on the button after.</summary>
        public float HoldProgress => IsCalibrated
            ? _buttonHold?.Progress ?? 0f
            : Mathf.Max(_restHold?.Progress ?? 0f, _buttonHold?.Progress ?? 0f);

        // How far the head may drift (m) during a hold before the count restarts.
        const float StillTolerance = 0.05f;

        float _floorY;
        float _startYaw;
        PoseHold _restHold;
        PoseHold _buttonHold;

        void Start()
        {
            if (Origin == null || Head == null || LeftHand == null || RightHand == null || Character == null)
            {
                Debug.LogError("[RigCalibrator] Missing references (Origin/Head/LeftHand/RightHand/Character). Disabling.", this);
                enabled = false;
                return;
            }
            var bones = new Transform[AnimUtils.boneNum];
            AnimUtils.BindBones(Character, ref bones);
            TargetHeight = RigCalibration.TPoseHeadsetHeight(bones, Character.GetComponentsInChildren<SkinnedMeshRenderer>()) - Margin;
            _floorY = Origin.position.y;
            _startYaw = Origin.eulerAngles.y;
            _restHold = new PoseHold(HoldSeconds, StillTolerance);
            _buttonHold = new PoseHold(HoldSeconds, StillTolerance);
        }

        // An action set up in the Inspector (not taken from an input actions asset) is this component's to enable.
        void OnEnable()
        {
            if (RecalibrateInput.reference == null) RecalibrateInput.action?.Enable();
        }

        void OnDisable()
        {
            if (RecalibrateInput.reference == null) RecalibrateInput.action?.Disable();
        }

        // LateUpdate: the tracked pose drivers have written this frame's poses.
        void LateUpdate()
        {
            var dt = Time.unscaledDeltaTime;
            var head = Head.position;
            var pressed = RecalibrateInput.action != null && RecalibrateInput.action.IsPressed();
            var due = _buttonHold.Update(pressed, head, dt);
            if (!IsCalibrated)
                due |= _restHold.Update(RigCalibration.IsAtRest(head, Head.up, LeftHand.position, RightHand.position), head, dt);
            if (due) Calibrate();
        }

        /// <summary>
        /// Calibrates now, whatever the pose: turns the rig about the head so the headset faces
        /// <see cref="FaceTarget"/> (if set), then moves it so the headset sits at <see cref="TargetHeight"/>.
        /// </summary>
        public void Calibrate()
        {
            if (FaceTarget != null) RigCalibration.TurnToFace(Origin, Head, FaceTarget.position);
            Origin.position += Vector3.up * (TargetHeight - HeadHeight);
            IsCalibrated = true;
            var facing = FaceTarget != null ? $"facing {FaceTarget.name} (rig turned {Turn:+0;-0}°), " : "";
            Debug.Log($"[RigCalibrator] {facing}headset at {TargetHeight:0.000} m above the floor (rig moved {Shift:+0.000;-0.000} m)", this);
            OnCalibrated();
        }

        void OnCalibrated() => Calibrated?.Invoke();
    }
}
