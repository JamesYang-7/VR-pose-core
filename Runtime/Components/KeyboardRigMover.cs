using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Desktop locomotion for the PC-mode multiplayer test: WASD translates and Q/E rotates a
/// target transform (the local rig). Runs before <see cref="SimulatedXrInput"/> (which reads
/// the rig as its <see cref="SimulatedXrInput.Base"/> frame) so the simulated head/hands —
/// and therefore the reconstructed, networked body — follow the movement the same frame.
/// </summary>
[DefaultExecutionOrder(-60)]
public sealed class KeyboardRigMover : MonoBehaviour
{
    /// <summary>Transform to move/rotate (defaults to this GameObject's transform).</summary>
    [field: SerializeField]
    [field: Tooltip("Transform to move/rotate. Defaults to this GameObject.")]
    public Transform Target { get; set; }

    /// <summary>Translation speed for WASD (metres/second).</summary>
    [field: SerializeField]
    [field: Tooltip("WASD translation speed (m/s).")]
    public float MoveSpeed { get; set; } = 1.5f;

    /// <summary>Turn speed for Q/E (degrees/second).</summary>
    [field: SerializeField]
    [field: Tooltip("Q/E turn speed (deg/s).")]
    public float TurnSpeed { get; set; } = 90f;

    void Awake()
    {
        if (Target == null) Target = transform;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float dt = Time.deltaTime;
        float turn = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
        if (turn != 0f) Target.Rotate(0f, turn * TurnSpeed * dt, 0f, Space.Self);

        float fwd = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        float strafe = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        Vector3 move = Target.forward * fwd + Target.right * strafe;
        move.y = 0f;
        if (move.sqrMagnitude > 1e-4f) Target.position += move.normalized * (MoveSpeed * dt);
    }
}
