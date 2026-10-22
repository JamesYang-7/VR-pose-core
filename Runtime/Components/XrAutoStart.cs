using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// Boots the XR (OpenXR) runtime at scene load. The project keeps <b>"Initialize XR on
/// Startup" OFF</b> so Multiplayer Play Mode's second editor doesn't fight over the single
/// OpenXR/Varjo runtime; this component starts XR explicitly instead — but only where a
/// headset should actually run.
///
/// Clone-safe: in a project with the Multiplayer Play Mode package, an MPPM virtual player
/// carrying <see cref="SkipTag"/> (default "Client") skips XR init, so a clone never grabs
/// the runtime. On a standalone build, or the untagged main editor, or a separate physical
/// machine, or a project without MPPM, it starts XR. Put one in any headset scene.
/// </summary>
public sealed class XrAutoStart : MonoBehaviour
{
    /// <summary>MPPM tag whose instances skip XR init (a clone can't own the single OpenXR runtime).</summary>
    [field: SerializeField]
    [field: Tooltip("MPPM tag whose instances skip XR init (a clone can't grab the single OpenXR runtime).")]
    public string SkipTag { get; set; } = "Client";

    bool _startedHere;

    void Start()
    {
#if UNITY_EDITOR && VRCHAT_MPPM
        // MPPM clone (e.g. tagged "Client"): don't init XR — the main editor owns the runtime.
        // Only with the MPPM package (the asmdef's version define): Unity 6.6's engine module compiles
        // CurrentPlayer without it, but there ReadOnlyTags() throws (no MPPM data store) and XR never started.
        if (System.Array.IndexOf(Unity.Multiplayer.PlayMode.CurrentPlayer.ReadOnlyTags(), SkipTag) >= 0)
        {
            Debug.Log($"[XrAutoStart] MPPM tag '{SkipTag}' present -> skipping XR init (clone can't own the runtime).");
            return;
        }
#endif
        var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (mgr == null)
        {
            Debug.LogError("[XrAutoStart] No XRManagerSettings for this build target; cannot start XR.");
            return;
        }
        if (mgr.activeLoader != null) return; // already running (e.g. Initialize-on-Startup got re-enabled)

        // InitializeLoaderSync (not the async InitializeLoader coroutine): this is a dedicated
        // VR scene started at load, so a brief synchronous init at startup is fine and avoids
        // bolting Awaitable onto XR Management's IEnumerator-based async API.
        mgr.InitializeLoaderSync();
        if (mgr.activeLoader == null)
        {
            Debug.LogError("[XrAutoStart] XR loader failed to initialize (no headset connected / runtime unavailable?).");
            return;
        }
        mgr.StartSubsystems();
        _startedHere = true;
        Debug.Log("[XrAutoStart] XR started.");
    }

    void OnDestroy()
    {
        // Only tear down XR if this component brought it up, so we don't stop a runtime that
        // "Initialize on Startup" (or another scene) owns.
        if (!_startedHere) return;
        var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (mgr == null || mgr.activeLoader == null) return;
        mgr.StopSubsystems();
        mgr.DeinitializeLoader();
    }
}
