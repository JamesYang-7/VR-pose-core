using UnityEngine;
using VrChat;

/// <summary>
/// What poses the 22-joint SMPL-X avatar live from the headset and two controllers, whatever solves the body: a pose
/// model (<see cref="PoseLiveWorker"/>, e.g. <see cref="RpmLiveWorker"/>) or IK alone (<see cref="IkLiveWorker"/>).
/// Holds the tracked transforms, the avatar, and the controllers' conversion to GORP's controller pose, from which
/// both read where the wrists are (<see cref="WristEstimate"/>).
///
/// <para>Both put the avatar's eyes on <see cref="Head"/> every rendered frame. Scripts that only place or re-pose
/// that body (a mirror, a seat, a retargeter) work with either.</para>
/// </summary>
public abstract class LiveWorker : MonoBehaviour
{
    /// <summary>HMD / head transform (the XR Origin Main Camera).</summary>
    [field: SerializeField]
    [field: Tooltip("HMD / head transform (XR Origin Main Camera).")]
    public Transform Head { get; set; }

    /// <summary>Left controller transform.</summary>
    [field: SerializeField]
    [field: Tooltip("Left controller transform.")]
    public Transform LeftController { get; set; }

    /// <summary>Right controller transform.</summary>
    [field: SerializeField]
    [field: Tooltip("Right controller transform.")]
    public Transform RightController { get; set; }

    /// <summary>Root GameObject of the SMPL-X avatar (must contain the named SMPL bones).</summary>
    [field: SerializeField]
    [field: Tooltip("Root of the SMPL-X avatar (contains the named SMPL bones).")]
    public Transform AvatarRoot { get; set; }

    /// <summary>Left controller position offset (m, the controller's own axes), applied before the rotation correction.</summary>
    [field: SerializeField]
    [field: Tooltip("Left controller → GORP's controller point, in metres along the controller's own axes, applied before the rotation correction. Default: Meta's grip → old pose translation (GorpDevice.GripOffset), for the OpenXR grip pose.")]
    public Vector3 LeftGripOffset { get; set; } = GorpDevice.GripOffset;

    /// <summary>Right controller position offset (m, the controller's own axes), applied before the rotation correction.</summary>
    [field: SerializeField]
    [field: Tooltip("Right controller → GORP's controller point, as for the left. Default: GorpDevice.GripOffset.")]
    public Vector3 RightGripOffset { get; set; } = GorpDevice.GripOffset;

    /// <summary>Left controller rotation correction (Euler), applied as rotation * Euler before conversion.</summary>
    [field: SerializeField]
    [field: Tooltip("Left controller → GORP's controller frame, applied as rotation * Euler. Default (60,0,0): Meta's grip → old pose turn (GorpDevice.GripCorrectionEuler), for the OpenXR grip pose; Quest wrist pivots measured 43–58°.")]
    public Vector3 LeftGripCorrectionEuler { get; set; } = GorpDevice.GripCorrectionEuler;

    /// <summary>Right controller rotation correction (Euler), applied as rotation * Euler before conversion.</summary>
    [field: SerializeField]
    [field: Tooltip("Right controller → GORP's controller frame, applied as rotation * Euler. Default (60,0,0), as for the left.")]
    public Vector3 RightGripCorrectionEuler { get; set; } = GorpDevice.GripCorrectionEuler;

    /// <summary>True once the avatar holds a pose from the tracking.</summary>
    public abstract bool HasPose { get; }

    /// <summary>True when the avatar's wrists are put on the controllers' wrist estimates every frame.</summary>
    public virtual bool PinsWrists => true;

    // Scratch for the joint conversion.
    readonly float[] _raw = new float[GorpDevice.RawWidth];
    readonly double[] _jointPosition = new double[3];
    readonly double[] _jointRotation = new double[9];

    /// <summary>
    /// The pose GORP recorded for a controller whose OpenXR grip pose is <paramref name="controller"/>'s:
    /// <see cref="GorpDevice.FromGrip"/> with that side's offset and correction.
    /// </summary>
    /// <param name="controller">The controller's transform (its grip pose).</param>
    /// <param name="device"><see cref="GorpDevice.Left"/> or <see cref="GorpDevice.Right"/>.</param>
    public Pose GorpControllerPose(Transform controller, int device)
    {
        var grip = new Pose(controller.position, controller.rotation);
        return device == GorpDevice.Left
            ? GorpDevice.FromGrip(grip, LeftGripOffset, Quaternion.Euler(LeftGripCorrectionEuler))
            : GorpDevice.FromGrip(grip, RightGripOffset, Quaternion.Euler(RightGripCorrectionEuler));
    }

    /// <summary>
    /// Where GORP's conversion puts the wrist joint for <paramref name="controller"/> (its grip pose, converted with
    /// that side's offset and correction, less GORP's controller-to-wrist offset), in Unity world.
    /// </summary>
    /// <param name="controller">The controller's transform (its grip pose).</param>
    /// <param name="device"><see cref="GorpDevice.Left"/> or <see cref="GorpDevice.Right"/>.</param>
    public Vector3 WristEstimate(Transform controller, int device) =>
        SmplJoint(GorpControllerPose(controller, device), device, Quaternion.identity).position;

    /// <summary>
    /// The SMPL joint GORP's conversion (RPM-README §4.2, <see cref="GorpDevice.ToSmpl"/>) puts at a device pose: the
    /// head joint for the headset, a wrist for a controller pose in GORP's convention (<see cref="GorpControllerPose"/>).
    /// Its Unity world position, and its world rotation on a SMPL-X rig whose root bone (the pelvis's parent) has
    /// world rotation <paramref name="rigRoot"/> (<see cref="GorpPose.WorldRotation"/>).
    /// </summary>
    protected Pose SmplJoint(Pose devicePose, int device, Quaternion rigRoot)
    {
        GorpDevice.FromUnity(devicePose.position, devicePose.rotation, 1f, _raw, 0);
        GorpDevice.ToSmpl(_raw, 0, device, _jointPosition, _jointRotation);
        var position = GorpPose.ToUnity(new Vector3((float)_jointPosition[0], (float)_jointPosition[1], (float)_jointPosition[2]));
        return new Pose(position, GorpPose.WorldRotation(_jointRotation, rigRoot));
    }
}
