# Live lip sync with NVIDIA Audio2Face

Design record for driving a Character Creator face from live audio with NVIDIA
Audio2Face-3D, written 2026-10-07 when the native plugin, the `VrChat.Audio2Face`
assembly and `Tools~/audio2face_plugin.py` landed. It replaces the Oculus LipSync →
`CcVisemeDriver` path on a CC body (see `smplx-lipsync.md`). Keep it current
when the plugin API, the morph table or the wiring changes.

## What it does

Audio2Face's regression model turns 16 kHz speech into per-frame face motion, and
the SDK's blendshape solver expresses each frame as 52 ARKit-style skin weights
(`jawOpen`, `mouthFunnel`, `browInnerUp`, …) plus 16 tongue weights
(`tongueTipUp`, …). `Audio2FaceDriver` streams the voice its `AudioSource` plays
into the model, maps those weights onto the character's CC5 morphs and writes them
every frame. Compared with Oculus visemes it moves the whole lower face (jaw, lip
corners, cheeks, brows, tongue), not just 15 mouth shapes.

## The pieces

| Where | What |
|---|---|
| `Native~/Audio2FacePlugin/` | C++ source of `a2f_unity.dll`: a flat C API (`include/a2f_unity.h`) over the Audio2X SDK, which is a C++ API (virtual interfaces, `std::error_code`) that P/Invoke cannot call. One session per face: `a2f_push_audio` (any thread) → worker thread (accumulate, execute, CPU blendshape solve, drop consumed audio) → `a2f_pop_frame` (main thread). `test/a2f_unity_test.cpp` drives it like Unity does, without Unity. |
| `Assets/Plugins/x86_64/Audio2Face/` (git-ignored) | `a2f_unity.dll` and NVIDIA's `audio2x.dll`, written by `Tools~/audio2face_plugin.py build`. |
| `Assets/StreamingAssets/Audio2Face/<character>/` (git-ignored) | The model: `model.json` and the 9 files it names (222 MB for James), copied by `Tools~/audio2face_plugin.py install-model`. `network.trt` is a TensorRT engine built for one GPU and TensorRT version. |
| `Runtime/Audio2Face/` (`VrChat.Audio2Face` asmdef, Editor + Windows x64 only) | `Audio2FaceSession` (the P/Invoke wrapper; a `SafeHandle` keeps a disposed session alive until the audio thread's call returns), `Audio2FaceDriver` (the component), and the pure parts: `AudioDownsampler` (device rate → 16 kHz mono, zero delay), `AudioDelayLine`, `FrameTimeline` (frames on the audio timeline, interpolated), `CcMorphMixer` (pose → morph table). |
| `Tests/Editor/Audio2Face/` | 33 unit tests for the pure parts and the default table, 3 integration tests (`Audio2FaceSessionTests`, inconclusive when the runtime or the model is missing). |
| `Assets/Scenes/Audio2FaceTest.unity` | Aaron alone, looping the Oculus sample clip through `Audio2FaceDriver`. |

## Setting up a machine

1. NVIDIA GPU, CUDA **12.8–12.x** (12.9 used here) and TensorRT **10.13–10.x** for
   that CUDA (10.16.1.11 used here). The CUDA and TensorRT `bin` folders must be on
   PATH.
2. Clone and build https://github.com/NVIDIA/Audio2Face-3D-SDK by its README
   (`fetch_deps.bat release`, `build.bat all release`, then in a Python 3.10 venv
   `download_models.bat` and `gen_testdata.bat`, which builds the TensorRT engines
   for this GPU). Pitfalls met on the first PC: `build.bat` picks the newest Visual
   Studio, and CUDA 12.x's nvcc rejects VS 2026 (`_MSC_VER >= 1950`), so point its
   `VS_PATH` at VS 2022; a Miniconda `Library\bin` on PATH makes CMake find
   Miniconda's zlib 1.2.13 instead of the SDK's 1.3.1; `uv pip install` needs
   `--index-strategy unsafe-best-match` for the SDK's `requirements.txt`; the
   Audio2Emotion model is gated on Hugging Face (accept the license, log in).
3. From this repo: `python Tools~/audio2face_plugin.py build --sdk <sdk clone> --test`
   and `python Tools~/audio2face_plugin.py install-model --sdk <sdk clone>`
   (or set `AUDIO2X_SDK_ROOT`).
4. **Restart Unity Hub and the editor** after installing CUDA/TensorRT or changing
   PATH: the editor inherits its environment from the Hub, and a stale one cannot
   find `nvinfer_10.dll`. The plugin then reports exactly which DLL is missing.

Rebuilding the plugin needs the editor closed if it has loaded the DLL (Play mode
or the integration tests): Windows keeps a loaded DLL locked until the process exits.

## Wiring a character

On the GameObject with the character's voice `AudioSource`: remove
`OVRLipSyncContext` and `CcVisemeDriver`, add `Audio2FaceDriver`, set `Face` to
`CC_Base_Body` and `Tongue` to `CC_Base_Tongue`. Keep the Reallusion `BoneDriver`
on `CC_Base_Body`: the jaw is a bone on a CC export, and the BoneDriver turns
`Jaw_Open` / `Jaw_Left` / `Jaw_Right` / `Jaw_Fwd` into jaw rotation (and copies body
weights to brows, lashes and stubble) in its LateUpdate, after the driver writes
the weights in Update.

The driver reads whatever the `AudioSource` plays (`OnAudioFilterRead`), so the
voice must reach an `AudioSource` on that body, as for Oculus. It loads the model
off the main thread when enabled (about 0.6 s) and frees it when disabled.

## Latency and `SyncAudio`

The model reads a window centred on each frame, so a frame for audio time t can
only be computed once audio up to about t + 0.26 s has arrived. Measured with the
native test pushing Unity-sized blocks in real time on the RTX 4090: **median
272 ms, p95 291 ms, max 324 ms** from a sample being pushed to its frame being
ready. If the voice plays as it arrives, the lips trail it by that much.

`SyncAudio` (on by default) makes the driver delay what its own `AudioSource`
plays by `AudioDelayMs` (300 ms), and it shows the frame for the audio that is
audible now: `pushed samples − delay`, interpolated between the 60 fps frames at
the render rate. Lips and voice then line up, at the cost of 300 ms more voice
latency. `LatencyMs` on the component shows the live value; keep `AudioDelayMs`
above it. With `SyncAudio` off the voice is immediate and the face shows the newest
frame, ~0.27 s late. Which one `NetPlayer` should use is open (PROGRESS.md).

Throughput is not the limit: one session runs at about 7× real time (~2.4 ms per
frame), and over a 5-minute stream it holds at most ~1.5 s of audio.

## Verified in Play (2026-10-07)

`Audio2FaceTest` in a freshly started editor (CUDA 12.9 and TensorRT 10.16 on its
PATH): the 3 `Audio2FaceSessionTests` pass inside the editor, and in Play the driver
reports `IsRunning` with `LatencyMs` 261–285. Sampled while the clip loops:
`Jaw_Open` 0–36 (100 scale), `Mouth_Funnel_UL` up to 16, `Mouth_Lips_Purse_UL` 10,
`Mouth_Lips_Press_L` 12, `Mouth_LowerLip_Depress_L` 4–30, `Mouth_UpperLip_Raise_L`
up to 16, `Brow_Raise_In_L` up to 5, and zero in the pauses. `CC_Base_JawRoot`
turns from 270° to ~262° at `Jaw_Open` 35, so the BoneDriver opens the jaw. Game-view
captures show closed lips in pauses and the teeth showing on open vowels. `Eye_Widen_*`
stays under 2: the staring look in that scene is Aaron's neutral face with nothing
driving blinks. Full EditMode suite 147/147.

## The morph table (`CcMorphMixer.DefaultBlends`)

Written from Aaron's CC5 Extended profile names, one entry per Audio2Face pose,
each a few weighted morphs (summed and clamped to [0, 1], like `CcVisemeMixer`).
The direct counterparts are solid (jaw, brows, dimples, stretch, press, lip
raise/depress, roll-in, funnel and purse quadrants); the shrug and tongue-roll
entries are guesses to tune by eye. `Intensity` scales every pose.

- Left out: `eyeBlink*` and `eyeLook*` (inactive in the SDK's solver configs, and
  the eyes and lids belong to eye tracking) and `noseSneer*` (the configs solve
  only the left side).
- `Eye_Squint_L/R` and `Eye_Wide_L/R` exist in Aaron's FBX but only on the lash
  and occlusion meshes; on `CC_Base_Body` they are `Eye_Squint_Inner_*` and
  `Eye_Widen_*`. `CcMorphMixerTests.DefaultBlends_EveryMorph_ExistsOnAaron` checks
  every table morph against `CC_Base_Body` and `CC_Base_Tongue`.
- The table is serialized on the component, so a scene keeps its own copy; later
  edits to the code default do not reach it.
