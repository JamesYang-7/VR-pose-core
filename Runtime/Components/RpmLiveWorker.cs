using Unity.InferenceEngine;
using UnityEngine;
using VrChat;

/// <summary>
/// The live pipeline (<see cref="PoseLiveWorker"/>) on an RPM step graph (<see cref="ModelExport.Rpm"/>,
/// RPM-README): one of the authors' released checkpoints, run by <see cref="RpmStepRunner"/>.
///
/// <para>From the version's manifest: an untracked hand is zeroed, with no reappearance rule (RPM-README §4.4;
/// the synthetic-input versions, threshold 0, mask nothing); the first step needs 11 input frames, so the avatar
/// gets its first pose about 0.37 s after the start, seeded from the standing pose turned to the head on that
/// frame; each step commits the newest frame. The network sees world positions as tracked, so its accuracy
/// depends on where the user stands (§7).</para>
/// </summary>
public sealed class RpmLiveWorker : PoseLiveWorker
{
    /// <summary>The RPM step graph.</summary>
    [field: SerializeField]
    [field: Tooltip("rpm_<name>.onnx (Models/rpm/models/<name>). gorp_real_inputs_reactive is the version trained on real device input.")]
    public ModelAsset Model { get; set; }

    /// <summary>The same version's manifest.json: windows, frame rate, input threshold and normalisation statistics.</summary>
    [field: SerializeField]
    [field: Tooltip("manifest.json next to Model: its windows, frame rate, input threshold and the normalisation statistics the seed needs.")]
    public TextAsset Manifest { get; set; }

    RpmManifest _manifest;

    protected override IPoseStepRunner CreateRunner()
    {
        if (Model == null || Manifest == null)
        {
            Debug.LogError("[RpmLiveWorker] Model (rpm_<name>.onnx) and Manifest (its manifest.json) are both needed. Disabling.", this);
            return null;
        }
        _manifest = RpmManifest.Parse(Manifest.text);
        return new RpmStepRunner(Model, _manifest);
    }

    protected override GorpInputProcessor CreateInputProcessor() =>
        new(HandMasking.Zero, reappearStaticFrames: 0, threshold: _manifest.ConfidenceThreshold);

    protected override float ModelFps => _manifest.Fps;
}
