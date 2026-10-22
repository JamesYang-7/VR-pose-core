using UnityEngine;

/// <summary>
/// Draws RGB world axes (X = red, Y = green, Z = blue) at this transform, for visual
/// debugging of the tracked VR points (head + controllers). Visible in the Scene view
/// always, and in the Game view when the toolbar's Gizmos toggle is enabled.
/// </summary>
public sealed class PoseAxisGizmo : MonoBehaviour
{
    /// <summary>Axis length in metres.</summary>
    [field: SerializeField]
    [field: Tooltip("Axis length in metres.")]
    public float AxisLength { get; set; } = 0.15f;

    void OnDrawGizmos()
    {
        Transform t = transform;
        Vector3 p = t.position;
        Gizmos.color = Color.red;   Gizmos.DrawLine(p, p + t.right * AxisLength);   // X
        Gizmos.color = Color.green; Gizmos.DrawLine(p, p + t.up * AxisLength);      // Y
        Gizmos.color = Color.blue;  Gizmos.DrawLine(p, p + t.forward * AxisLength); // Z
    }
}
