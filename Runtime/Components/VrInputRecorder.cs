using System.Collections.Generic;
using System.IO;
using VrChat;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Records the three tracked transforms (head + two controllers) each frame during a VR
/// session and saves them to a NumPy <c>.npy</c> file for offline analysis or PC replay.
/// Captures raw Unity world-space pose (position + rotation quaternion) — exactly what the
/// headset reported, before any training-frame conversion or controller correction — so the
/// recording is a faithful source for analysis.
///
/// Output: a <c>(frames, 22)</c> float32 array, one row per frame, columns:
/// <code>
///   0      : time (s, since recording started)
///   1..3   : head position (x, y, z)
///   4..7   : head rotation quaternion (x, y, z, w)
///   8..10  : left controller position
///   11..14 : left controller rotation
///   15..17 : right controller position
///   18..21 : right controller rotation
/// </code>
/// Records from Play start by default; the desktop operator can also toggle with a key.
/// Always flushes on stop/quit so a session is never lost.
/// </summary>
public sealed class VrInputRecorder : MonoBehaviour
{
    /// <summary>HMD / head transform to record.</summary>
    [field: SerializeField] public Transform Head { get; set; }

    /// <summary>Left controller transform to record.</summary>
    [field: SerializeField] public Transform LeftController { get; set; }

    /// <summary>Right controller transform to record.</summary>
    [field: SerializeField] public Transform RightController { get; set; }

    /// <summary>Begin recording as soon as Play starts.</summary>
    [field: SerializeField]
    [field: Tooltip("Begin recording as soon as Play starts.")]
    public bool RecordOnPlay { get; set; } = true;

    /// <summary>Output folder, relative to the project root (created if missing).</summary>
    [field: SerializeField]
    [field: Tooltip("Output folder, relative to the project root (created if missing).")]
    public string OutputFolder { get; set; } = "Recordings";

    /// <summary>Keyboard key to toggle recording on/off (desktop operator).</summary>
    [field: SerializeField]
    [field: Tooltip("Keyboard key to toggle recording on/off from the desktop.")]
    public Key ToggleKey { get; set; } = Key.R;

    /// <summary>Auto-save and start a new file every this many frames (keeps memory bounded
    /// and lets the session run indefinitely). ~32768 frames ≈ 7 min at 80 fps.</summary>
    [field: SerializeField]
    [field: Tooltip("Auto-save and roll to a new file every this many frames (0 = never roll).")]
    public int MaxFramesPerFile { get; set; } = 32768;

    /// <summary>True while frames are being recorded.</summary>
    public bool IsRecording => _recording;

    /// <summary>Full path of the last file written; empty before the first one.</summary>
    public string LastSavedPath { get; private set; } = "";

    const int Stride = 22; // time(1) + 3 x (pos3 + quat4)
    readonly List<float> _buf = new();
    bool _recording;
    float _startTime;
    int _frames;

    void Start()
    {
        if (RecordOnPlay) StartRecording();
    }

    void LateUpdate()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[ToggleKey].wasPressedThisFrame)
        {
            if (_recording) StopRecording();
            else StartRecording();
        }

        if (!_recording || Head == null || LeftController == null || RightController == null)
            return;

        _buf.Add(Time.time - _startTime);
        Capture(Head);
        Capture(LeftController);
        Capture(RightController);
        _frames++;

        // Roll to a new file at the size cap so the session can run indefinitely.
        if (MaxFramesPerFile > 0 && _frames >= MaxFramesPerFile)
        {
            WriteSegment();
            _buf.Clear();
            _frames = 0;
            _startTime = Time.time;
        }
    }

    void OnDisable() => StopRecording(); // flush on play-stop / quit

    /// <summary>Begin a fresh recording (discards any unsaved buffer).</summary>
    public void StartRecording()
    {
        _buf.Clear();
        _frames = 0;
        _startTime = Time.time;
        _recording = true;
        Debug.Log("[VrInputRecorder] recording started.");
    }

    /// <summary>Stop recording and write the buffered frames to disk.</summary>
    public void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        WriteSegment();
    }

    // Flush the current buffer to a timestamped .npy (no-op if empty). Does not change
    // recording state, so it serves both StopRecording and the auto-roll.
    void WriteSegment()
    {
        if (_frames == 0) { Debug.LogWarning("[VrInputRecorder] nothing recorded; no file written."); return; }

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputFolder));
        WriteFormatNote(root);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(root, $"vrinput_{stamp}.npy");
        NpyWriter.SaveFloat32(path, _buf.ToArray(), new[] { _frames, Stride });
        LastSavedPath = path;
        Debug.Log($"[VrInputRecorder] saved {_frames} frames ({_frames / Mathf.Max(0.001f, Time.time - _startTime):F0} fps avg) -> {path}");
    }

    void Capture(Transform t)
    {
        Vector3 p = t.position;
        Quaternion q = t.rotation;
        _buf.Add(p.x); _buf.Add(p.y); _buf.Add(p.z);
        _buf.Add(q.x); _buf.Add(q.y); _buf.Add(q.z); _buf.Add(q.w);
    }

    static void WriteFormatNote(string folder)
    {
        Directory.CreateDirectory(folder);
        string note = Path.Combine(folder, "format.txt");
        if (File.Exists(note)) return;
        File.WriteAllText(note,
            "vrinput_*.npy : float32 array, shape (frames, 22), C-order. Unity world space, raw.\n" +
            "Columns per row:\n" +
            "  0      time (s, since recording start)\n" +
            "  1..3   head position (x, y, z)\n" +
            "  4..7   head rotation quaternion (x, y, z, w)\n" +
            "  8..10  left controller position\n" +
            "  11..14 left controller rotation\n" +
            "  15..17 right controller position\n" +
            "  18..21 right controller rotation\n");
    }
}
