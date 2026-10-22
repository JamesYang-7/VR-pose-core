using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Parks the two controller transforms at a fixed pose relative to the head whenever the
/// real hand controllers aren't tracked, so the pose pipeline reconstructs a stable
/// standing body (hands resting at the sides) instead of feeding on untracked controller
/// transforms sitting at the origin. When a controller IS tracked, this yields to its
/// TrackedPoseDriver and does nothing — plug controllers in and the arms come alive with
/// no scene change.
///
/// Runs in <c>LateUpdate</c> with an execution order ahead of the live worker (<see cref="PoseLiveWorker"/>)
/// (which reads the controller poses in its own <c>LateUpdate</c>), so the stubbed pose is
/// what the pipeline sees this frame.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class StubUntrackedHands : MonoBehaviour
{
    /// <summary>Head transform the stubbed hands are placed relative to.</summary>
    [field: SerializeField]
    [field: Tooltip("Head transform the stubbed hands are placed relative to.")]
    public Transform Head { get; set; }

    /// <summary>Left controller transform to stub while untracked.</summary>
    [field: SerializeField]
    [field: Tooltip("Left controller transform to stub while untracked.")]
    public Transform LeftController { get; set; }

    /// <summary>Right controller transform to stub while untracked.</summary>
    [field: SerializeField]
    [field: Tooltip("Right controller transform to stub while untracked.")]
    public Transform RightController { get; set; }

    /// <summary>Resting left-hand offset from the head, in the head's yaw frame: out, down, slightly forward.</summary>
    [field: SerializeField]
    [field: Tooltip("Left-hand rest offset from the head (yaw frame): -X out, -Y down, +Z forward.")]
    public Vector3 LeftOffset { get; set; } = new Vector3(-0.2f, -0.55f, 0.15f);

    /// <summary>Resting right-hand offset from the head, in the head's yaw frame.</summary>
    [field: SerializeField]
    [field: Tooltip("Right-hand rest offset from the head (yaw frame): +X out, -Y down, +Z forward.")]
    public Vector3 RightOffset { get; set; } = new Vector3(0.2f, -0.55f, 0.15f);

    /// <summary>Force the stub even when a controller reports tracked (handy to pin a fixed pose).</summary>
    [field: SerializeField]
    [field: Tooltip("Force the stub even when a controller is tracked.")]
    public bool AlwaysStub { get; set; }

    void LateUpdate()
    {
        if (Head == null) return;
        if (LeftController != null && (AlwaysStub || !IsTracked(XRNode.LeftHand)))
            PlaceHand(LeftController, LeftOffset);
        if (RightController != null && (AlwaysStub || !IsTracked(XRNode.RightHand)))
            PlaceHand(RightController, RightOffset);
    }

    void PlaceHand(Transform hand, Vector3 headYawLocalOffset)
    {
        // Yaw-only head basis: hands hang at the sides regardless of head pitch/roll, so
        // looking up or down doesn't fling the arms around.
        Quaternion flat = Quaternion.Euler(0f, Head.eulerAngles.y, 0f);
        hand.SetPositionAndRotation(Head.position + flat * headYawLocalOffset, flat);
    }

    static bool IsTracked(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        return device.isValid
            && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked)
            && tracked;
    }
}
