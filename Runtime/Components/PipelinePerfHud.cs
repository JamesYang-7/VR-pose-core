using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VrChat.Audio2Face;

/// <summary>
/// Frame budget readout for scenes that run a pose model and Audio2Face together: frame time, the
/// main thread's and the GPU's share of it (<see cref="FrameTimingManager"/>), the main-thread
/// cost and the latency of each pose step, and Audio2Face's latency, as mean / p95 / max since the
/// last <see cref="ResetStats"/>, counted from <see cref="WarmupSeconds"/> after the first frame.
/// Drawn on screen and returned by <see cref="Summary"/>.
///
/// <para>Pins <see cref="Application.targetFrameRate"/> while enabled, standing in for the
/// headset's refresh rate: the editor runs with vSync off, and GPU load measured uncapped says
/// nothing about a frame budget. The pose model steps on its own clock (30 Hz) at any frame rate.</para>
///
/// <para>Unity cannot see GPU memory or the CUDA work Audio2Face does on its own thread; read
/// those from outside (Windows "GPU Process Memory" / "GPU Engine" counters).</para>
/// </summary>
public sealed class PipelinePerfHud : MonoBehaviour
{
    /// <summary>The pose pipeline to time; may be empty or disabled.</summary>
    [field: SerializeField]
    [field: Tooltip("The live pose worker to time. Leave empty (or disable it) to measure without one.")]
    public PoseLiveWorker Worker { get; set; }

    /// <summary>The Audio2Face driver to watch; may be empty or disabled.</summary>
    [field: SerializeField]
    [field: Tooltip("The Audio2Face driver whose latency is shown. Leave empty (or disable it) to measure without Audio2Face.")]
    public Audio2FaceDriver Audio2Face { get; set; }

    /// <summary>Frame rate held while enabled; 0 leaves it alone.</summary>
    [field: SerializeField]
    [field: Tooltip("Application.targetFrameRate while enabled (0 = leave it); stands in for the headset's refresh rate. The pose model steps at its own rate regardless.")]
    public int TargetFrameRate { get; set; } = 30;

    /// <summary>Seconds after the first frame before samples are kept.</summary>
    [field: SerializeField]
    [field: Tooltip("Seconds after the first frame before samples are kept. The first frames carry the Play-mode load and the pipelines' start-up, seconds each.")]
    public float WarmupSeconds { get; set; } = 5f;

    /// <summary>Draw the readout in the Game view.</summary>
    [field: SerializeField]
    public bool ShowOnScreen { get; set; } = true;

    // How often the on-screen text is rebuilt; sorting the samples every frame is wasted work.
    const float TextRefreshSeconds = 0.5f;

    // A frame counts as late past this multiple of the target interval: without vSync the editor's
    // frame times jitter by a millisecond or two around the target, and those are not misses.
    const float LateFactor = 1.25f;

    readonly List<float> _frameMs = new();
    readonly List<float> _mainThreadMs = new();
    readonly List<float> _gpuMs = new();
    readonly List<float> _stepMs = new();
    readonly List<float> _stepLatencyMs = new();
    readonly List<float> _a2fLatencyMs = new();
    readonly FrameTiming[] _timing = new FrameTiming[1];
    int _previousTargetFrameRate;
    double _countFrom = -1; // realtime from which samples are kept; set on the first frame
    int _lastCommitted;
    float _nextText;
    string _text = "";

    void OnEnable()
    {
        _previousTargetFrameRate = Application.targetFrameRate;
        if (TargetFrameRate > 0) Application.targetFrameRate = TargetFrameRate;
        _countFrom = -1;
        ResetStats();
    }

    void OnDisable() => Application.targetFrameRate = _previousTargetFrameRate;

    void Update()
    {
        // The first frame's delta spans the whole Play-mode load and the next two carry the
        // pipelines' start-up (8.6 s, 1.8 s and 0.8 s in one test scene). Counted, they held
        // the mean near 20 fps for a minute while the scene ran at 30.
        var now = Time.realtimeSinceStartupAsDouble;
        if (_countFrom < 0) _countFrom = now + WarmupSeconds;
        if (now < _countFrom)
        {
            _text = $"warming up, counting in {_countFrom - now:F1} s";
            return;
        }

        _frameMs.Add(Time.unscaledDeltaTime * 1000f);

        // Timings arrive four frames late; a zero GPU time means the platform reports none.
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
        {
            if (_timing[0].cpuMainThreadFrameTime > 0) _mainThreadMs.Add((float)_timing[0].cpuMainThreadFrameTime);
            if (_timing[0].gpuFrameTime > 0) _gpuMs.Add((float)_timing[0].gpuFrameTime);
        }

        if (Worker != null && Worker.isActiveAndEnabled && Worker.CommittedFrames != _lastCommitted)
        {
            _lastCommitted = Worker.CommittedFrames;
            _stepMs.Add((float)Worker.LastInferenceMs);
            _stepLatencyMs.Add((float)Worker.LastLatencyMs);
        }
        if (Audio2Face != null && Audio2Face.isActiveAndEnabled && Audio2Face.IsRunning && Audio2Face.LatencyMs > 0)
            _a2fLatencyMs.Add(Audio2Face.LatencyMs);

        if (ShowOnScreen && Time.unscaledTime >= _nextText)
        {
            _text = Summary();
            _nextText = Time.unscaledTime + TextRefreshSeconds;
        }
    }

    void OnGUI()
    {
        if (!ShowOnScreen) return;
        GUI.Box(new Rect(10, 10, 520, 136), GUIContent.none);
        GUI.Label(new Rect(18, 14, 510, 132), _text);
    }

    /// <summary>Drops every sample, e.g. after the warm-up.</summary>
    public void ResetStats()
    {
        _frameMs.Clear();
        _mainThreadMs.Clear();
        _gpuMs.Clear();
        _stepMs.Clear();
        _stepLatencyMs.Clear();
        _a2fLatencyMs.Clear();
        _lastCommitted = Worker != null ? Worker.CommittedFrames : 0;
    }

    /// <summary>One line per measure: mean / p95 / max in milliseconds and the sample count.</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        var fps = _frameMs.Count > 0 ? 1000f / Mean(_frameMs) : 0f;
        sb.Append($"{fps:F1} fps (target {(TargetFrameRate > 0 ? TargetFrameRate.ToString() : "none")})");
        if (TargetFrameRate > 0 && _frameMs.Count > 0)
        {
            var budget = LateFactor * 1000f / TargetFrameRate;
            var late = 0;
            foreach (var ms in _frameMs) if (ms > budget) late++;
            sb.Append($", {100f * late / _frameMs.Count:F1}% of frames > {budget:F1} ms");
        }
        sb.AppendLine("   mean / p95 / max ms");
        Line(sb, "frame", _frameMs);
        Line(sb, "main thread", _mainThreadMs);
        Line(sb, "GPU frame", _gpuMs);
        Line(sb, "pose main thr.", _stepMs);
        Line(sb, "pose latency", _stepLatencyMs);
        Line(sb, "A2F latency", _a2fLatencyMs);
        return sb.ToString();
    }

    static void Line(StringBuilder sb, string label, List<float> samples)
    {
        if (samples.Count == 0)
        {
            sb.AppendLine($"{label,-14} n/a");
            return;
        }
        var sorted = new List<float>(samples);
        sorted.Sort();
        var p95 = sorted[Mathf.Min(sorted.Count - 1, (int)(0.95f * sorted.Count))];
        sb.AppendLine($"{label,-14} {Mean(samples),6:F2} / {p95,6:F2} / {sorted[^1],6:F2}   (n={samples.Count})");
    }

    static float Mean(List<float> samples)
    {
        var sum = 0.0;
        foreach (var s in samples) sum += s;
        return (float)(sum / samples.Count);
    }
}
