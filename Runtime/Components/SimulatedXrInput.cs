using UnityEngine;

/// <summary>
/// Procedurally animates the head + two controller transforms for a headset-free PC
/// test of the pose pipeline (no real XR device needed). Offers a few distinct motion
/// <see cref="MotionPreset"/>s so two networked peers can be told apart at a glance — the
/// owner's <see cref="VrChat.Net.NetworkedPoseSync"/> assigns a preset per client id
/// at spawn, so the host and client visibly move differently.
///
/// Writes in <c>Update</c> so the poses are set before the live worker's (<see cref="PoseLiveWorker"/>)
/// <c>LateUpdate</c> reads them. Use in a scene where the rig's TrackedPoseDrivers are
/// disabled (otherwise they fight these writes).
/// </summary>
public sealed class SimulatedXrInput : MonoBehaviour
{
    /// <summary>Distinct canned motions for distinguishing peers while debugging.</summary>
    public enum MotionPreset
    {
        /// <summary>Asymmetric circular hand sweep + gentle head sway.</summary>
        Wave = 0,

        /// <summary>Symmetric overhead arm raise/lower (jumping-jack style).</summary>
        JumpingJack = 1,
    }

    /// <summary>Number of defined presets (for round-robin assignment by client id).</summary>
    public const int PresetCount = 2;

    /// <summary>Head / HMD transform to drive.</summary>
    [field: SerializeField] public Transform Head { get; set; }

    /// <summary>Left controller transform to drive.</summary>
    [field: SerializeField] public Transform LeftController { get; set; }

    /// <summary>Right controller transform to drive.</summary>
    [field: SerializeField] public Transform RightController { get; set; }

    /// <summary>Which canned motion to play.</summary>
    [field: SerializeField]
    [field: Tooltip("Which canned motion to play (set per peer at runtime when networked).")]
    public MotionPreset Preset { get; set; } = MotionPreset.Wave;

    /// <summary>Optional frame the motion is played in: when set, the head/hand offsets are
    /// placed relative to this transform, so moving/rotating it (e.g. via keyboard) carries the
    /// whole simulated person. Null = world-absolute around the origin (original behaviour).</summary>
    [field: SerializeField]
    [field: Tooltip("Optional base frame; move/rotate it to move the simulated person. Null = world-absolute.")]
    public Transform Base { get; set; }

    /// <summary>When true, the motion freezes in place (time stops advancing) and resumes smoothly.</summary>
    [field: SerializeField]
    [field: Tooltip("Freeze the motion in place; resumes from the same phase.")]
    public bool Paused { get; set; }

    /// <summary>Animation speed multiplier.</summary>
    [field: SerializeField]
    [field: Tooltip("Animation speed multiplier.")]
    public float Speed { get; set; } = 1.5f;

    /// <summary>Hand motion radius in metres.</summary>
    [field: SerializeField]
    [field: Tooltip("Hand motion radius (m).")]
    public float Reach { get; set; } = 0.25f;

    /// <summary>Standing eye height in metres.</summary>
    [field: SerializeField]
    [field: Tooltip("Standing eye height (m).")]
    public float EyeHeight { get; set; } = 1.6f;

    // Own accumulated animation clock so Paused holds the pose (freezing Time.time would stop
    // every SimulatedXrInput, and we want per-instance pause).
    float _t;

    void Update()
    {
        if (!Paused) _t += Time.deltaTime * Speed;
        switch (Preset)
        {
            case MotionPreset.JumpingJack: UpdateJumpingJack(_t); break;
            default: UpdateWave(_t); break;
        }
    }

    // Place a driven transform using an offset expressed in the local frame, mapped through
    // Base when set (so a moved/rotated Base carries the whole person), else world-absolute.
    void Place(Transform target, Vector3 pos, Quaternion rot)
    {
        if (target == null) return;
        if (Base != null) target.SetPositionAndRotation(Base.TransformPoint(pos), Base.rotation * rot);
        else target.SetPositionAndRotation(pos, rot);
    }

    // Preset 0: gentle head sway with each hand tracing a small circle, left/right out of phase.
    void UpdateWave(float t)
    {
        Place(Head,
            new Vector3(0.03f * Mathf.Sin(t * 0.5f), EyeHeight, 0f),
            Quaternion.Euler(8f * Mathf.Sin(t * 0.3f), 12f * Mathf.Sin(t * 0.25f), 0f));

        float shoulderY = EyeHeight - 0.4f;
        Place(LeftController,
            new Vector3(-0.2f - Reach * 0.5f * Mathf.Sin(t), shoulderY + Reach * Mathf.Sin(t * 1.3f), 0.25f + Reach * Mathf.Cos(t)),
            Quaternion.Euler(0f, -40f, 20f * Mathf.Sin(t)));

        Place(RightController,
            new Vector3(0.2f + Reach * 0.5f * Mathf.Sin(t + 1f), shoulderY + Reach * Mathf.Sin(t * 1.3f + 1f), 0.25f + Reach * Mathf.Cos(t + 1f)),
            Quaternion.Euler(0f, 40f, -20f * Mathf.Sin(t)));
    }

    // Preset 1: both arms sweep together from down-by-the-sides up to overhead, in the
    // frontal plane — clearly different from the asymmetric circular Wave.
    void UpdateJumpingJack(float t)
    {
        float lift = Mathf.Sin(t) * 0.5f + 0.5f; // 0 = arms down, 1 = arms up/out

        Place(Head,
            new Vector3(0f, EyeHeight + 0.04f * Mathf.Sin(t * 2f), 0f),
            Quaternion.Euler(6f * Mathf.Sin(t * 0.5f), 0f, 0f));

        float handY = (EyeHeight - 0.55f) + 0.7f * lift; // ~1.05 m (down) .. ~1.75 m (up)
        float spread = 0.28f + 0.34f * lift;             // arms move outward as they rise
        float roll = 70f * lift - 25f;                   // hands roll outward through the sweep

        Place(LeftController, new Vector3(-spread, handY, 0.08f), Quaternion.Euler(0f, 0f, roll));
        Place(RightController, new Vector3(spread, handY, 0.08f), Quaternion.Euler(0f, 0f, -roll));
    }
}
