# SMPL-X mesh and Oculus LipSync

Design record for the avatar mesh and the lip-sync path. Written when the
mouth-bag FBX, the `SMPLX` pose-corrective component, the mouth
postprocessor and the Oculus LipSync install landed. Keep it current when the
mesh, the material split or the lip-sync wiring changes.

## The FBX

`Assets/SMPLX/smplx-visemes-unity.fbx` is the only avatar. It and
`Assets/SMPLX/Textures/` are git-ignored (with their `.meta` files) and travel in
the project's local-files zip (its `tools/pack_local_files.py` and `README.md`). The current bytes
are a Blender export that adds a **mouth bag** so the open mouth is a cavity
rather than a hole (the earlier export had none: with the jaw open the skybox
showed through and read as a white mouth). The `.meta` is stable: GUID
`5f34f92c6fba86348bce0c2519c51e32`, so `NetPlayer.prefab`, the scenes
and `GazeRayVisualizer.cs` resolve it. The 22 SMPL bones, the two eye joints
and the mesh name `SMPLX-mesh-male` are unchanged from the previous export.

Verified after a forced reimport in 6000.6.1f1:

| Property | Value |
|---|---|
| Vertices | 12550 |
| Triangles | 21196 (20908 body + 288 mouth interior) |
| Submeshes | 2 |
| Blendshapes | 520 (0–9 betas `Shape000–009`, 10–19 expressions `Exp000–009`, 20–505 pose correctives `Pose000–485`, 506–519 visemes) |
| Materials on the imported renderer | `SMPLX-male-material`, `SMPLX-MouthInterior` |
| `NetPlayer.prefab` SkinnedMeshRenderer | mesh resolves, 2 material slots |

Viseme blendshape order at 506–519: PP FF TH DD kk CH SS nn RR aa E ih oh ou
(Oculus order with no `sil`).

## `Assets/SMPLX/` (from the official MPI SMPL-X Unity package)

- `Scripts/SMPLX.cs` — pose correctives (486 blendshapes recomputed in
  `LateUpdate` from the current joint rotations), betas, expressions, presets.
  Imported verbatim; do not restyle. Compiles into `Assembly-CSharp`.
- `ThirdParty/JSON`, `ThirdParty/Matrix` — its dependencies.
- `Resources/smplx_betas_to_joints_{male,female,neutral}.json` — loaded by name
  from `Resources`, so keep them there.
- `Materials/` — four URP Lit body materials (`SMPLX-Male-URP`,
  `SMPLX-Male-URP-AgentB`, `SMPLX-Female-URP-AgentA`, `SMPLX-Female-URP-AgentB`)
  and `SMPLX-MouthInterior.mat`.
- `Textures/` — stock SMPL-X albedo and normal maps for both sexes and two
  SMPLitex albedos. SMPL-X model license is research use; the sample textures
  are CC BY-NC. `LICENSE.md` and `README.md` are there for that reason.

## The mouth bag and why it needs the postprocessor

The bag is unmapped: all 161 of its vertices, and the inner-lip rim merged
into it, sit on UV (0, 0), so the whole cavity samples one texel, the
bottom-left corner of whatever albedo the body wears. That corner is dark in
the stock SMPL-X atlas but near-white in SMPLitex-generated textures, so an
untreated bag renders black on one texture and grey on another. The
postprocessor below splits those 288 triangles onto submesh 1 with
`SMPLX-MouthInterior.mat` (URP Lit, base colour 0.055/0.02/0.02, smoothness 0,
specular highlights and environment reflections off, back-face culling on) so
the cavity is texture-independent. This was chosen over splitting the material
in Blender (a round trip per model revision) and over painting the texture
corner dark (repeated for every new texture).

Consequences:
- Every body renderer gets a second material slot from the FBX. Code that
  assigns a body material must write element 0 only and leave element 1 alone.
- If a body ever shows a white or skin-coloured mouth, check that the mesh has
  two submeshes; if not, the postprocessor was not compiled when the FBX
  imported, and a Reimport fixes it.
- Edit-mode skinning stalls while the editor is unfocused, so a viseme set by
  script or by the Open Jaw menu will not show until you click into the editor.

## `Editor/`

- `SmplxMouthInteriorSubmesh.cs` (namespace `VrChat.Editor`) — an
  `AssetPostprocessor` keyed on the literal path
  `Assets/SMPLX/smplx-visemes-unity.fbx`. At import it moves every triangle
  whose three vertices sit on UV (0, 0) onto submesh 1 and assigns
  `SMPLX-MouthInterior.mat`. It must be compiled **before** the FBX imports;
  if the mesh ever shows `subMeshCount == 1`, right-click the FBX and Reimport.
  Change the cavity's darkness on the material, never on the mesh.
- `VisemeTools.cs` — menu `Tools → SMPL-X → Visemes → Open Jaw / Close Jaw`,
  drives blendshape 515 (`aa`) in Edit Mode so you can look into the mouth.

## `Assets/Oculus/LipSync/` (third-party, git-ignored)

The plugin is not committed. `/Assets/Oculus/` is in `.gitignore`; each
machine installs it from
<https://developers.meta.com/horizon/documentation/unity/audio-ovrlipsync-unity/>
as `README.md` describes. Scenes reference its scripts and the sample clip by
the package's own GUIDs, which a verbatim import preserves.

What the project uses from it: the `Oculus.LipSync` and `Oculus.LipSync.Editor`
asmdefs with `Scripts/` and `Editor/`, `Assets/OfflineModel/` (the analyser
model), `Plugins/Win64/` and `Audio/vox_lp_01.wav` (the vendor sample, the
`LipSyncTest` clip). The sample `Models/`, `Scenes/`, `Prefabs/` and the
non-Windows `Plugins/` are not needed. Neither asmdef references anything in
this project, so it compiles standalone.

## How to wire lip sync on a body

1. One GameObject in the scene with an `OVRLipSync` component. Without it every
   context analyses nothing and no mouth moves.
2. On the body GameObject: an `AudioSource`, an `OVRLipSyncContext` with
   `audioLoopback` **on** (off, and the voice is analysed but never heard), and
   an `OVRLipSyncContextMorphTarget` whose `skinnedMeshRenderer` is the body's
   renderer.
3. `visemeToBlendTargets`: index 0 (`sil`) = -1, indices 1..14 = 506..519.
4. `laughterBlendTarget` = **-1**. The default 15 is `Exp005`, an expression
   blendshape on this mesh, and would drive it.
5. `SMPLX` component on the body with `usePoseCorrectives` on, `modelType`
   `Male` (it selects the betas-to-joints regressor, not the mesh surface).

The context analyses whatever its `AudioSource` plays. For MetaVoiceChat the
decoded remote voice has to reach an `AudioSource` on that peer's body rather
than a mixer-only path, otherwise there is nothing to read.

## Verified in Play (single player)

`Assets/Scenes/LipSyncTest.unity` is the wiring above on one body, no networking:
`OVRLipSync` object, the FBX instantiated as `SMPLX Body` with an `AudioSource`
looping `Assets/Oculus/LipSync/Audio/vox_lp_01.wav` (36.9 s, 48 kHz mono),
`OVRLipSyncContext` (`audioLoopback` on), `OVRLipSyncContextMorphTarget` mapped
506–519 with laughter −1, and `SMPLX` (`Male`, pose correctives on); a camera
0.45 m in front of the head. Press Play and the mouth talks along with the clip.
Untick the Multiplayer Play Mode virtual player first, or the clone plays the
scene too and both editors drive the speakers.

Observed: `OVRLipSync.IsInitialized() == Success`, the context's phoneme frame
carries non-zero viseme scores, and the viseme blendshape weights change from
frame to frame (e.g. `DD 32 / kk 31 / SS 16`, later `PP 72 / ih 12`, zero in
the pauses). Screenshots show the lips parting and the teeth and mouth bag
visible. Console clean apart from the four `[SMPL-X]` setup logs. The pose
correctives are live too (`Pose4xx` weights of 25–73 at the bind pose). One
editor gotcha: the UnityMCP screenshot tool pauses Play mode, so unpause
(`EditorApplication.isPaused = false`) before sampling again.

## Mouth bag rendering fix

With the jaw open the cavity showed bright radial streaks instead of a dark
hollow. The geometry and the material assignment were fine (288 triangles on
submesh 1, normals facing into the cavity, `SMPLX-MouthInterior` on slot 1);
the streaks were the URP Lit shader's **specular highlights and skybox
environment reflections** at grazing angles, which a smoothness of 0 does not
switch off. `SMPLX-MouthInterior.mat` now has `_SpecularHighlights = 0` and
`_EnvironmentReflections = 0` (keywords `_SPECULARHIGHLIGHTS_OFF`,
`_ENVIRONMENTREFLECTIONS_OFF`). Verified in Play in `LipSyncTest` with the
`aa` viseme at 100 against a green camera background: the cavity is a uniform
dark red-black with only faint diffuse shading, and nothing shows through. If
a scene lights the cavity directly (no shadow-casting light), it will read
slightly brighter; that is the base colour, tune it on the material.

## Character Creator bodies (Aaron) — `CcVisemeDriver`

A CC5 export has no Oculus-named visemes, so `OVRLipSyncContextMorphTarget`
does not fit: it maps each Oculus viseme to one blendshape, and when two map to
the same one it overwrites instead of adding. What Aaron has instead:

| Where | What |
|---|---|
| `CC_Base_Body` (365 shapes) | the 8 CC lip visemes at indices 0–7: `V_Open`, `V_Explosive`, `V_Dental_Lip`, `V_Tight_O`, `V_Tight`, `V_Wide`, `V_Affricate`, `V_Lip_Open` |
| `CC_Base_Tongue` (33 shapes) | the 7 tongue visemes `V_Tongue_up`, `_Raise`, `_Out`, `_Narrow`, `_Lower`, `_Curl_U`, `_Curl_D` |
| `BoneDriver` on `CC_Base_Body` (Reallusion CC Unity Tools, git-ignored under `Assets/Reallusion/`) | in LateUpdate, turns body weights into bone motion (`V_Open` → `CC_Base_JawRoot` 16° at 100) and copies them to stubble, brows, lashes, tear line and eye occlusion |

The jaw is a bone, not a morph: `V_Open` alone parts the lips, and the
`BoneDriver` opens the jaw. So the adapter only writes weights.

`CcVisemeDriver` (on the character root; it lives in each project's `Assets/Scripts/`,
not in this package, because it needs Oculus LipSync, which each machine installs
into the project) reads the context's viseme frame each
Update, runs `CcVisemeMixer.Mix` over its `Blends` table (one entry per Oculus
viseme: a few weighted CC shapes, summed and clamped to [0, 1]), writes lip
shapes to `Face` (`CC_Base_Body`) and tongue shapes to `Tongue`
(`CC_Base_Tongue`). It sets the context's smoothing (70) like the morph-target
component does. Without a `BoneDriver`, the lips move but the jaw stays shut.

The default table (`CcVisemeMixer.DefaultBlends`) was chosen by eye in
`LipSyncTest`, posing each Oculus viseme at full strength on SMPL-X (native
Oculus shapes) and on Aaron side by side. What the CC lip shapes look like:
`V_Lip_Open` parts the lips over closed teeth (base of SS, DD, nn, TH, kk);
`V_Wide` spreads them with little gap (E, ih, with some `V_Lip_Open`);
`V_Affricate` flares them forward (CH, RR); `V_Tight_O` puckers (oh, ou);
`V_Explosive` presses them (PP); `V_Dental_Lip` tucks the lower lip (FF).
`V_Tight` presses closed lips together and matches no Oculus viseme, so it is
unused. TH shows the tongue tip between the teeth. The table is serialized on
the component; the scene copy does not follow later edits to the code default.

Wiring a CC body: an `AudioSource` + `OVRLipSyncContext` on it (plus the one
`OVRLipSync` object in the scene), then `CcVisemeDriver` with `Context`, `Face`
and `Tongue` set. Verified in Play (`LipSyncTest`): live frames
such as Oculus `E` 0.55 → `V_Wide` 23 / `V_Lip_Open` 65 / `V_Open` 25 with the
jaw at 4° and the stubble following. The console showed one vendor exception
after stopping Play: `OVRLipSyncContext.ProcessAudioSamples` tests
`audioSource == null` on the audio thread, which Unity 6.6 rejects
(`EnsureRunningOnMainThread`) once the object is torn down. It is in the
git-ignored plugin, affects every context, and is harmless at shutdown.

## Not done yet

- Aaron is not on `NetPlayer` yet; when it is, its `CcVisemeDriver` needs the
  peer's decoded voice `AudioSource`, same as the SMPL-X note below.

- `NetPlayer` has no lip-sync components and no `SMPLX` component; the body
  still wears `SMPLX-male-material` from the FBX rather than one of the URP
  materials in `Materials/`. Wiring it there, fed by the peer's decoded voice
  `AudioSource`, is the next step (see `PROGRESS.md`).
