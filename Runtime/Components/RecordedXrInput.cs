using System.IO;
using VrChat;
using UnityEngine;

/// <summary>
/// Replays a recorded VR session (<c>vrinput_*.npy</c> from <see cref="VrInputRecorder"/>)
/// by driving the head + two controller transforms from the file — a headset-free way to
/// feed a live worker (<see cref="PoseLiveWorker"/>) real tracking on the PC, so the reconstruction can be
/// iterated on deterministically. Sibling of <see cref="SimulatedXrInput"/>: writes in
/// <c>Update</c> so the poses are set before the worker's <c>LateUpdate</c> reads them.
///
/// The recording can be (re)loaded at runtime via <see cref="Load"/> — enable
/// <see cref="ShowPicker"/> to get an on-screen path field, which lets each peer in a
/// Multiplayer Play Mode session pick its own recording independently (an Inspector value
/// is shared across virtual players, so it can't be set per-player there).
///
/// The recording's first frames (before SteamVR delivers poses) sit at the origin; those
/// are skipped so playback starts on the first valid frame.
/// </summary>
public sealed class RecordedXrInput : MonoBehaviour
{
    /// <summary>Head / HMD transform to drive.</summary>
    [field: SerializeField] public Transform Head { get; set; }

    /// <summary>Left controller transform to drive.</summary>
    [field: SerializeField] public Transform LeftController { get; set; }

    /// <summary>Right controller transform to drive.</summary>
    [field: SerializeField] public Transform RightController { get; set; }

    /// <summary>Recording path, relative to the project root.</summary>
    [field: SerializeField]
    [field: Tooltip("Recording path relative to the project root.")]
    public string RecordingPath { get; set; } = "Recordings/samples/tpose.npy";

    /// <summary>Load <see cref="RecordingPath"/> on Start (off if you pick one at runtime).</summary>
    [field: SerializeField] public bool AutoLoadOnStart { get; set; } = true;

    /// <summary>Show an on-screen field to type/load a recording path (per instance, e.g. MPPM peers).</summary>
    [field: SerializeField]
    [field: Tooltip("Show an on-screen field to pick a recording per instance (each MPPM peer chooses independently).")]
    public bool ShowPicker { get; set; }

    /// <summary>Loop playback when the recording ends.</summary>
    [field: SerializeField] public bool Loop { get; set; } = true;

    /// <summary>Playback speed multiplier.</summary>
    [field: SerializeField] public float Speed { get; set; } = 1f;

    /// <summary>Skip leading frames where the head sits at the origin (pre-tracking).</summary>
    [field: SerializeField] public bool SkipLeadingUntracked { get; set; } = true;

    /// <summary>Recenter the clip so the first head frame sits at x=z=0 facing +Z (a "local"
    /// clip), letting it be placed anywhere via <see cref="RootTransform"/>. Height is kept.</summary>
    [field: SerializeField] public bool RecenterToStart { get; set; } = true;

    /// <summary>If set, the (recentered) signal is placed relative to this transform, so the
    /// motion rides the player's object root. Null = drive in world space about the origin.</summary>
    [field: SerializeField] public Transform RootTransform { get; set; }

    const int Stride = 22;
    float[] _data;
    int _frames;
    int _start;      // first played row (after skipping untracked)
    float _t0;       // recording time of the first played row
    float _clock;    // elapsed playback time since the first played row
    string _status = "no recording loaded";
    Vector3 _recenterPos;                          // start head (x,0,z), subtracted to recenter
    Quaternion _recenterRot = Quaternion.identity; // yaw that turns the start facing to +Z

    /// <summary>True once a recording is loaded and ready to drive transforms.</summary>
    public bool Ready { get; private set; }

    void Start()
    {
        if (AutoLoadOnStart) Load(RecordingPath);
    }

    /// <summary>
    /// Load a recording (path relative to the project root) and restart playback from its
    /// first tracked frame. Returns false (and leaves the previous recording, if any) on
    /// a missing file or bad shape. Safe to call at runtime.
    /// </summary>
    public bool Load(string pathOrRelative)
    {
        // Absolute paths resolve identically across instances — use these under Multiplayer
        // Play Mode, whose virtual players run from a clone folder that doesn't contain the
        // (project-root, gitignored) Recordings/. Relative paths resolve against the project
        // root of *this* instance.
        string path = Path.IsPathRooted(pathOrRelative)
            ? pathOrRelative
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", pathOrRelative));
        if (!File.Exists(path)) { _status = "not found: " + path; Debug.LogError("[RecordedXrInput] " + _status); return false; }

        NpyArray arr;
        try { arr = NpyReader.LoadFloat32(path); }
        catch (System.Exception e) { _status = "load error: " + e.Message; Debug.LogError("[RecordedXrInput] " + _status); return false; }

        if (arr.Shape.Length != 2 || arr.Shape[1] != Stride)
        {
            _status = $"expected (frames,{Stride}), got {arr.ShapeString()}";
            Debug.LogError("[RecordedXrInput] " + _status);
            return false;
        }

        RecordingPath = pathOrRelative;
        _data = arr.Data;
        _frames = arr.Shape[0];
        _start = SkipLeadingUntracked ? FirstTrackedRow() : 0;
        _t0 = _data[_start * Stride];
        _clock = 0f;
        ComputeRecenter();
        _status = $"loaded {_frames} frames — {Path.GetFileName(path)}";
        Ready = true;
        return true;
    }

    // From the first played frame's head, build the transform that puts it at x=z=0 facing +Z
    // (yaw only, height preserved).
    void ComputeRecenter()
    {
        int o = _start * Stride;
        var headPos = new Vector3(_data[o + 1], _data[o + 2], _data[o + 3]);
        var headRot = new Quaternion(_data[o + 4], _data[o + 5], _data[o + 6], _data[o + 7]);
        _recenterPos = new Vector3(headPos.x, 0f, headPos.z);
        Vector3 fwd = headRot * Vector3.forward; fwd.y = 0f;
        float yaw = fwd.sqrMagnitude > 1e-6f ? Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg : 0f;
        _recenterRot = Quaternion.Euler(0f, -yaw, 0f);
    }

    int FirstTrackedRow()
    {
        for (int r = 0; r < _frames; r++)
        {
            int o = r * Stride;
            if (Mathf.Abs(_data[o + 1]) > 1e-4f || Mathf.Abs(_data[o + 2]) > 1e-4f || Mathf.Abs(_data[o + 3]) > 1e-4f)
                return r;
        }
        return 0;
    }

    void Update()
    {
        if (!Ready) return;

        _clock += Time.deltaTime * Speed;
        float target = _t0 + _clock;

        // Advance to the row whose timestamp brackets the playback clock.
        int r = _start;
        while (r + 1 < _frames && _data[(r + 1) * Stride] <= target) r++;

        if (r >= _frames - 1 && target > _data[(_frames - 1) * Stride])
        {
            if (Loop) { _clock = 0f; r = _start; }
            else { r = _frames - 1; }
        }

        Apply(r);
    }

    void OnGUI()
    {
        if (!ShowPicker) return;
        GUILayout.BeginArea(new Rect(12, 262, 460, 92), GUI.skin.box);
        GUILayout.Label("Recording path (absolute, or relative to project root):");
        RecordingPath = GUILayout.TextField(RecordingPath);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Load", GUILayout.Width(70))) Load(RecordingPath);
        GUILayout.Label(_status);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void Apply(int row)
    {
        int o = row * Stride;
        Set(Head, o + 1);
        Set(LeftController, o + 8);
        Set(RightController, o + 15);
    }

    void Set(Transform t, int off)
    {
        if (t == null) return;
        Vector3 p = new Vector3(_data[off], _data[off + 1], _data[off + 2]);
        Quaternion q = new Quaternion(_data[off + 3], _data[off + 4], _data[off + 5], _data[off + 6]);

        // Recenter to a local clip (start head at x=z=0 facing +Z), then place relative to the
        // player root so the motion travels with the player.
        if (RecenterToStart)
        {
            p = _recenterRot * (p - _recenterPos);
            q = _recenterRot * q;
        }
        if (RootTransform != null)
        {
            p = RootTransform.TransformPoint(p);
            q = RootTransform.rotation * q;
        }
        t.SetPositionAndRotation(p, q);
    }
}
