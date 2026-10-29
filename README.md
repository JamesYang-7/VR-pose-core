# VR Pose Core

Unity package `io.github.jamesyang-7.vr-pose-core`: full-body pose from sparse VR tracking (headset and two
controllers in, a 22-joint SMPL body out), shared by the projects built on it.

- **Pose models** on Unity's Inference Engine: RPM's released checkpoints (`RpmStepRunner`, 30 fps; Barquero et
  al., CVPR 2025) behind `IPoseStepRunner`, run live by `RpmLiveWorker` on the `PoseLiveWorker` base. A project can
  add its own model the same way: a runner, a `PoseLiveWorker` subclass and a `ModelExport` for its files.
  `PoseLayout` holds the input frame and motion row layouts they share.
- **IK only**: `IkLiveWorker` poses the same body with no model, no model files and no GPU work (`Runtime/Ik/`:
  `IkBody`, the closed-form `TwoBoneIk`, `SpineLean`): eyes on the headset, wrists on the controllers, the upper body
  bent to reach them, the legs straight. Both workers share the `LiveWorker` base (the tracked transforms, the avatar,
  the grip conventions), so whatever places or re-poses that body works with either.
- **Device input**: `GorpDevice` and `GorpInputProcessor` turn headset and controller poses into model frames;
  XR input from the live rig, a recording (`RecordedXrInput`, `VrInputRecorder`), a simulation or a GORP capture.
- **Conversions and data**: SMPL (Z-up) ↔ Unity (`MathUtils`, `GorpPose`), `.npy` / `.npz` readers and writer.
- **Body placement**: `EyeAnchor`, rig calibration (`RigCalibration`, `RigCalibrator`), SMPL → character
  retargeting (`Runtime/Retarget/`).
- **Lip sync**: NVIDIA Audio2Face through a native plugin, and Oculus visemes mixed into CC visemes (the
  component that reads Oculus LipSync, `CcVisemeDriver`, stays in the projects, next to that install).
- **Tools**: `XrAutoStart`, the pipeline perf HUD, skeleton and axis debug views, a mirror avatar, the GORP clip
  baker and the retarget fit.

## Use it in a project

Add it as a git submodule in the project's `Packages/` folder, under the package's name:

```
git submodule add <repo url> Packages/io.github.jamesyang-7.vr-pose-core
```

Unity treats a package folder inside `Packages/` as an embedded package: its code is editable in the project, and
changes are committed in this repo, inside the submodule. The project records which commit it uses. Clone a project
that uses it with `git clone --recurse-submodules`, or run `git submodule update --init` after cloning.

To list the package's tests in the Test Runner, add it to `testables` in the project's `Packages/manifest.json`:

```json
"testables": [ "io.github.jamesyang-7.vr-pose-core" ]
```

### What the project provides

- **Unity 6000.6, Windows x64.** `VrChat.Audio2Face` and `VrChat.Components` compile for the Editor and Windows
  x64 standalone only.
- **The SMPL-X avatar** at `Assets/SMPLX/smplx-visemes-unity.fbx`, which the clip baker, the mouth-interior tool and
  `GorpPoseTests` / `RigCalibrationTests` / `SmplRetargetTests` load, and the character
  `Assets/Characters/Aaron-SMPL/`, which `RetargetFit`, `CcMorphMixerTests` and `SmplRetargetTests` load. Baked clips
  go to `Assets/Generated/`.
- **The Audio2Face runtime**: `Tools~/audio2face_plugin.py` builds the plugin into the project's
  `Assets/Plugins/x86_64/Audio2Face/` and copies a model into `Assets/StreamingAssets/Audio2Face/`
  (`Documentation~/audio2face-lipsync.md`).

## Model files

`Models/<export>/` holds each export (`rpm/`): its README (the contract) and each version's `manifest.json`
are in git; the ONNX graphs, the GORP captures (`data/`) and each version's reference output (`output_ref/`) are
git-ignored and come from the projects' local-files zip. Keep each graph's `.meta` with it: scenes reference the
graphs by the GUID in it. `ModelExport` finds the exports at `Packages/io.github.jamesyang-7.vr-pose-core/Models`.

`Models/model_fingerprint.txt` holds the SHA-256 of every git-ignored model file, and `ModelFingerprintTests` fails
when the local files, or the code's model settings, don't match it. After installing a new export, run
`python Tools~/model_fingerprint.py` and commit the file.

## Layout

| Path | What |
|---|---|
| `Runtime/` | `VrChat.Runtime`: the step runners, input processing, conversions, IO, calibration, lip sync (`LipSync/`), retargeting (`Retarget/`) and IK (`Ik/`) |
| `Runtime/Audio2Face/` | `VrChat.Audio2Face`: the plugin's P/Invoke wrapper, `Audio2FaceDriver` and its pure parts |
| `Runtime/Components/` | `VrChat.Components`: the MonoBehaviours scenes use (live workers, model and IK, XR input, recorder, debug views, HUD, `XrAutoStart`) |
| `Editor/` | `VrChat.Editor`: the GORP clip baker, `RetargetFit`, the viseme and mouth-interior tools |
| `Tests/Editor/` | `VrChat.EditModeTests`: EditMode tests; the model tests skip when the model files are missing |
| `Models/` | The model exports (above) |
| `Native~/Audio2FacePlugin/` | C++ source of the Audio2Face plugin |
| `Tools~/` | `audio2face_plugin.py`, `model_fingerprint.py`, the CC5 slider fit (`cc5/`) |
| `Documentation~/` | Audio2Face lip sync, the SMPL-X avatar and Oculus lip sync |

Folders ending in `~` are not imported by Unity.

## License

The package's own code and docs are under the MIT License (`LICENSE.md`). It does not cover what the package
loads from elsewhere, which keeps its own license: RPM's checkpoints in `Models/rpm/` (the RPM authors' release,
see `RPM-README.md` there), NVIDIA's Audio2Face SDK and models (installed per machine by `Tools~/audio2face_plugin.py`),
and the project's avatars.
