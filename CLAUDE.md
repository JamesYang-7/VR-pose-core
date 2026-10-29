# CLAUDE.md

Guidance for Claude Code when working in this package. `README.md` has what it holds, how a project uses it and
where the model files go; the model contract is `Models/rpm/RPM-README.md`.

## What this is

The code shared by two Unity projects: a Varjo project and a Quest project, each with this repo as a submodule at
`Packages/io.github.jamesyang-7.vr-pose-core`. It reconstructs a full body from sparse VR tracking (headset and two
controllers → 22-joint SMPL pose) with networks exported to ONNX and run with Unity's Inference Engine. The shared
model is RPM (the authors' released checkpoints); a project can bring its own model on the same base. **The model
frame rate is 30 FPS**: a live worker steps its model on its own 30 Hz clock with an asynchronous readback, whatever
the headset's render rate (72–90 Hz), which it must not stall.

What stays in the projects: their own models, XR loader and runtime choice, headset-specific code (Varjo eye
tracking, the OpenXR runtime check), networking, scenes, prefabs, settings, the avatars' assets, and the Oculus
LipSync glue (`CcVisemeDriver`).

## Build and test

There is no CI and no project here: the package compiles inside a project that embeds it. Run the tests there, with
the package in `testables` (README.md): the Test Runner, or from Bash in that project
`unity command --timeout 300 --result-only run_tests --mode EditMode`. `RpmStepRunnerTests` replays a reference
sequence through the graph and checks the rollout against the stored one, the template for validating a new ONNX
model. `ModelFingerprintTests` ties the git-ignored model files to the commit.

## Architecture

### Inference pipeline pattern

The Inference Engine lifecycle (`RpmStepRunner`, the pattern for any new runner, behind `IPoseStepRunner`):
- Setup: `ModelLoader.Load(modelAsset)` → `new Worker(model, BackendType.GPUCompute)`, allocate `Tensor<float>`
  inputs with fixed `TensorShape`s, `SetInput`, load source data.
- Per step: `tensor.Upload(data)` → `worker.Schedule()` → read the outputs back → rotations → drive the skeleton.
  `DownloadToArray()` blocks on the GPU; live code requests an asynchronous readback and collects it on a later
  frame (`Begin` / `TryComplete`) through `TensorReadback.Download`. An async readback can complete with an error,
  for example while Audio2Face's CUDA work shares the GPU; `TensorReadback.Download` then reads the result again
  synchronously, where a plain `DownloadToArray` would throw.

The API namespace is `Unity.InferenceEngine` (package `com.unity.ai.inference`).

RPM's step graph takes 54-float input frames built from raw device poses (`PoseLayout.InputWidth`, RPM-README §2
and §4): 11 input frames per step (the first step is on frame 10, each step commits its newest input frame), a
normalised rotation state the graph returns and `RpmStepRunner` feeds back unchanged, zero-masked hands, no
canonicalisation. Outputs: `motion_out` (198 floats: 22×6D rotations, local except the root, then 22 world joint
positions, `PoseLayout.MotionWidth`) and `joint_rotmat`.

### Live workers: model or IK

`LiveWorker` (`Runtime/Components/`) is what a scene's live body hangs on: the head and controller transforms, the
avatar, the grip offsets and corrections, `HasPose`, and the controllers' wrist estimates. Two kinds extend it:
- `PoseLiveWorker` (and `RpmLiveWorker` on it) runs a model on its 30 Hz clock (the pattern above).
- `IkLiveWorker` solves the body by IK alone every rendered frame, with `IkBody` (`Runtime/Ik/`).

Both put the eyes on the headset and the wrists on the controllers, so scripts that place or re-pose the body (a
mirror, a seat, a retargeter) should take a `LiveWorker`.

`IkBody` starts from the rest pose each frame; the legs stay straight. The heading follows the head past a dead zone.
The pelvis keeps its first-frame height and follows the eyes past a radius. `SpineLean` (weighted shares) leans the
spine to the eyes. Each collar shrugs on long reaches. `TwoBoneIk` (closed form, about the rest hinge) bends each
elbow down and back. The head and wrists take the rotations GORP's conversion gives (`GorpPose.WorldRotation`). Why
not cyclic coordinate descent for the arms (`AnimUtils.SolveTwoBoneIK`, which the model worker pins with): it keeps
the bend of the pose it starts from, and IK starts from rest.

The wrist rotation that conversion gives is nominal. On GORP's captures it is 20–70° from the mocap wrist, depending
on the grip.

### Conversions and the skeleton

`Runtime/MathUtils.cs` (namespace `MyUtils`) is the coordinate glue. Training data is in Blender/SMPL axes (Z-up,
right-handed), Unity is left-handed Y-up: `BlenderToUnity` / `UnityToBlender` (positions, which `GorpPose.ToUnity`
and `GorpDevice` build on), `GlobalRotationUnityToBlender` (rotations), and `SixDToQuaternion` for the 6D rotation
representation. `AnimUtils` holds the 22-joint SMPL layout: `boneNames` is the canonical order, `boneNum = 22`, and
`BindBones` finds each bone by recursive name search, so bones must be named exactly as in `boneNames`.

Data IO is NumPy only: `NpyReader` for typed, shape-checked `.npy`, `NpzArchive` for `.npz`; little-endian float32.

## Conventions and gotchas

- **Two projects use every public member.** A change to a public API, a serialized field or a component's behaviour
  lands in both on their next submodule update: keep both building, or mark the old member `[Obsolete]` first.
- **Nothing project-, model- or headset-specific that a project keeps private.** No Varjo, OpenXR-runtime or
  networking code, and no private model's code, files or docs: a project plugs those in (a runner, a
  `PoseLiveWorker` subclass, a `ModelExport` for its own folder).
- **No hardcoded absolute paths.** Model data comes through `ModelExport` (`Packages/<name>/Models`, or a project's
  folder); files a project owns (recordings, baked clips, avatars) are relative to the project.
- `PoseLayout`'s sizes and `RpmStepRunner`'s windows must match the exported ONNX tensor shapes; `RpmStepRunner`
  checks the graph's inputs against the version's `manifest.json` when it loads.
- When a pose looks mirrored or rotated wrong, suspect a missing conversion (`GorpDevice`, `GorpPose`, `MathUtils`)
  or a controller grip correction before the model.
- `Runtime/Components/` keeps the global namespace it had in the projects: scenes reference these scripts by the
  GUIDs in their `.meta` files, which must move with them.
