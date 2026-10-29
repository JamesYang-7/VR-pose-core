using System.IO;
using UnityEngine;
using VrChat;

/// <summary>
/// Replays a GORP reference capture's raw device stream (<c>sparse_raw</c> in
/// <c>Models/rpm/data/gorp/*.npz</c>, RPM-README §6) into the head and controller transforms, as a
/// headset would deliver them, so a live worker (<see cref="LiveWorker"/>: a pose model or IK) runs on a real GORP capture without a headset.
/// Sibling of <see cref="RecordedXrInput"/>, which replays our own recordings; writes in <c>Update</c> so the
/// poses are set before the worker's <c>LateUpdate</c> reads them.
///
/// <para>Each raw pose goes back to Unity through <see cref="GorpDevice.ToUnity"/>, and the controllers have
/// <see cref="Worker"/>'s grip offsets and corrections taken off (<see cref="GorpDevice.ToGrip"/>), so the worker's own conversion gives back the capture's
/// raw poses. Frames are 30 fps and interpolated in between, since the worker samples them on its own 30 Hz
/// clock. A controller that lost tracking holds its last pose, as a tracked-pose driver does.</para>
///
/// <para>The capture is turned and moved so its first head pose sits at x = z = 0 facing +Z (height kept),
/// then placed relative to <see cref="RootTransform"/>. It loops; the jump back to the start (0.27 m of head
/// for s11) is a glitch the model recovers from.</para>
/// </summary>
public sealed class GorpSequenceXrInput : MonoBehaviour
{
    /// <summary>Head / HMD transform to drive.</summary>
    [field: SerializeField] public Transform Head { get; set; }

    /// <summary>Left controller transform to drive.</summary>
    [field: SerializeField] public Transform LeftController { get; set; }

    /// <summary>Right controller transform to drive.</summary>
    [field: SerializeField] public Transform RightController { get; set; }

    /// <summary>Capture to play: a file name under <c>Models/rpm/data/gorp</c>, without extension.</summary>
    [field: SerializeField]
    [field: Tooltip("File name under Models/rpm/data/gorp, without .npz. s11 has arm motion and no dropouts, and its head stays within 0.55 m of the start.")]
    public string SequenceName { get; set; } = "s11_beatsaber-q3-controllers_900";

    /// <summary>The worker these poses feed; its grip offsets and corrections are taken off the controllers.</summary>
    [field: SerializeField]
    [field: Tooltip("The live worker (e.g. RpmLiveWorker or IkLiveWorker) these poses feed. Its grip offsets and corrections are taken off the controllers, so it gets back the capture's raw poses. Empty: none taken off.")]
    public LiveWorker Worker { get; set; }

    /// <summary>If set, the recentred capture is placed relative to this transform; null = about the world origin.</summary>
    [field: SerializeField] public Transform RootTransform { get; set; }

    /// <summary>Playback speed multiplier.</summary>
    [field: SerializeField] public float Speed { get; set; } = 1f;

    const int kFrame = GorpDevice.Points * GorpDevice.RawWidth;

    float[] _raw;
    int _frames; // raw frames: the model frames plus the one before the first
    float _clock;
    Vector3 _origin;
    Quaternion _turn;

    void Start()
    {
        string path = Path.Combine(ModelExport.Rpm.DataDir, SequenceName + ".npz");
        if (!File.Exists(path))
        {
            Debug.LogError($"[GorpSequenceXrInput] {path} not found: extract the local-files zip.", this);
            enabled = false;
            return;
        }
        var seq = ModelExport.Rpm.LoadSequence(SequenceName);
        _raw = seq.SparseRaw;
        _frames = seq.FrameCount + 1;

        var head = GorpDevice.ToUnity(_raw, GorpDevice.Head * GorpDevice.RawWidth);
        _origin = new Vector3(head.position.x, 0f, head.position.z);
        var forward = head.rotation * Vector3.forward;
        _turn = Quaternion.Euler(0f, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 0f);
        Apply(0, 0f);
    }

    void Update()
    {
        _clock = Mathf.Repeat(_clock + Time.deltaTime * Speed, (_frames - 1) / GorpSequence.Fps);
        float f = _clock * GorpSequence.Fps;
        int k = Mathf.Min((int)f, _frames - 2);
        Apply(k, f - k);
    }

    // The poses between raw frames k and k + 1.
    void Apply(int k, float t)
    {
        Set(Head, k, GorpDevice.Head, t, Vector3.zero, Vector3.zero);
        if (Worker != null)
        {
            Set(LeftController, k, GorpDevice.Left, t, Worker.LeftGripOffset, Worker.LeftGripCorrectionEuler);
            Set(RightController, k, GorpDevice.Right, t, Worker.RightGripOffset, Worker.RightGripCorrectionEuler);
        }
        else
        {
            Set(LeftController, k, GorpDevice.Left, t, Vector3.zero, Vector3.zero);
            Set(RightController, k, GorpDevice.Right, t, Vector3.zero, Vector3.zero);
        }
    }

    // Writes the device's pose with the worker's grip offset and correction taken off (GorpDevice.ToGrip).
    void Set(Transform target, int k, int device, float t, Vector3 gripOffset, Vector3 gripCorrectionEuler)
    {
        if (target == null) return;
        int a = k * kFrame + device * GorpDevice.RawWidth, b = a + kFrame;
        bool aValid = _raw[a + 7] > 0f, bValid = _raw[b + 7] > 0f;
        if (!aValid && !bValid) return; // tracking lost: hold the last pose
        var from = GorpDevice.ToUnity(_raw, aValid ? a : b);
        var to = GorpDevice.ToUnity(_raw, bValid ? b : a);
        var p = _turn * (Vector3.Lerp(from.position, to.position, t) - _origin);
        var q = _turn * Quaternion.Slerp(from.rotation, to.rotation, t);
        if (RootTransform != null)
        {
            p = RootTransform.TransformPoint(p);
            q = RootTransform.rotation * q;
        }
        var grip = GorpDevice.ToGrip(new Pose(p, q), gripOffset, Quaternion.Euler(gripCorrectionEuler));
        target.SetPositionAndRotation(grip.position, grip.rotation);
    }
}
