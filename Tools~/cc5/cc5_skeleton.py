"""Reads a Character Creator 5 avatar's skeleton in the Unity export's convention, and probes how CC5's
body sliders move it — without exporting (the CC5 trial allows few exports).

Runs *inside* CC5 (RLPy), through the cc5 MCP `exec_python` tool:

    exec(open(r"E:/Unity_Projects/VR-Chat/tools/cc5/cc5_skeleton.py", encoding="utf-8").read())
    result = dump_skeleton(r"E:/Unity_Projects/VR-Chat/CC5/aaron_skeleton.json")
    result = probe_sliders(r"E:/Unity_Projects/VR-Chat/CC5/aaron_sliders.json")

Convention: for every skin bone, the joint's offset from its parent joint, expressed in the parent's
world axes, in metres, with x negated. That equals the bind `localPosition` of the same bone in the
Unity FBX export (the export mirrors x; CC5 works in cm, Z-up). Why not `LocalTransform().T()`: it
includes CC5's bone scale, which the export bakes away.

`RetargetFit.cs` (Unity editor) applies these vectors to an Aaron instance to simulate a slider
setting; `Tools~/cc5/fit_sliders.py` fits slider values from the probe.

Files are written as lists of {"name", "p": [x, y, z]} rather than dicts, so Unity's JsonUtility
(no dictionary support) can read them.
"""
import json

import RLPy

# CC5 renames its root at export.
UNITY_NAME = {"RL_BoneRoot": "CC_Base_BoneRoot"}

# Body sliders that can move joints (display name -> morph ID). The probe keeps the ones that do.
SLIDERS = {
    "Character Scale A": "2018-09-14-22-07-28_character scale",
    "Arm Length": "cc embed morphs/embed_arm1",
    "Upper Arm Length": "cc embed morphs/embed_arm4",
    "Forearm Length": "cc embed morphs/embed_arm5",
    "Shoulder Width A": "cc embed morphs/embed_arm102",
    "Shoulder Scale": "cc embed morphs/embed_arm101",
    "Chest Scale": "cc embed morphs/embed_torso104",
    "Chest Width": "cc embed morphs/embed_torso105",
    "Chest Height": "cc embed morphs/embed_torso112",
    "Hip Length A": "cc embed morphs/embed_torso4",
    "Hip Width A": "2018-09-14-22-09-23_pelvis width",
    "Leg Length": "cc embed morphs/embed_leg1",
    "Thigh Length": "cc embed morphs/embed_leg4",
    "Lower Leg Length": "cc embed morphs/embed_leg5",
    "Foot Length": "cc embed morphs/embed_foot2",
    "Neck Length": "cc embed morphs/embed_torso109",
    "Head Scale": "cc embed morphs/embed_full_head9",
}


def _avatar():
    return RLPy.RScene.GetAvatars()[0]


def _rotate_inverse(q, v):
    """Rotates v by the inverse of unit quaternion q = (x, y, z, w)."""
    x, y, z, w = -q[0], -q[1], -q[2], q[3]
    # v' = v + 2w(u x v) + 2u x (u x v), u = (x, y, z)
    cx = y * v[2] - z * v[1]
    cy = z * v[0] - x * v[2]
    cz = x * v[1] - y * v[0]
    return [
        v[0] + 2 * (w * cx + y * cz - z * cy),
        v[1] + 2 * (w * cy + z * cx - x * cz),
        v[2] + 2 * (w * cz + x * cy - y * cx),
    ]


def bone_vectors(av=None):
    """{unity bone name: [x, y, z]} — each skin bone's bind localPosition in the Unity export (metres)."""
    av = av or _avatar()
    out = {}
    for b in av.GetSkeletonComponent().GetSkinBones():
        p = b.GetParent()
        if p is None:
            continue
        tb, tp = b.WorldTransform(), p.WorldTransform()
        d = [tb.T().x - tp.T().x, tb.T().y - tp.T().y, tb.T().z - tp.T().z]
        r = tp.R()
        v = _rotate_inverse((r.x, r.y, r.z, r.w), d)
        out[UNITY_NAME.get(b.GetName(), b.GetName())] = [-v[0] / 100.0, v[1] / 100.0, v[2] / 100.0]
    return out


def _qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def bone_rotations(av=None):
    """{unity bone name: [x, y, z, w]} — each skin bone's rotation relative to its parent, in CC5's own
    bone axes (mirrored like the positions). Not the export's bind localRotation: the exporter rolls
    many bones about their own axis (up to 62°), which moves no joint. Use it to check that sliders
    don't turn bones (they turn none by more than 0.2°); joint positions are all the retarget's
    position errors depend on (RetargetFit gives identical numbers with the frames rolled)."""
    av = av or _avatar()
    out = {}
    for b in av.GetSkeletonComponent().GetSkinBones():
        p = b.GetParent()
        if p is None:
            continue
        # Keep the RTransforms alive while reading: R() of a temporary dangles (reads garbage).
        tb, tp = b.WorldTransform(), p.WorldTransform()
        rb, rp = tb.R(), tp.R()
        x, y, z, w = _qmul((-rp.x, -rp.y, -rp.z, rp.w), (rb.x, rb.y, rb.z, rb.w))
        out[UNITY_NAME.get(b.GetName(), b.GetName())] = [x, -y, -z, w]
    return out


def slider_weights(av=None):
    sc = (av or _avatar()).GetAvatarShapingComponent()
    return {name: sc.GetShapingMorphWeight(mid) for name, mid in SLIDERS.items()}


def set_sliders(values, av=None):
    """values: {display name: weight}. Returns the weights read back."""
    av = av or _avatar()
    sc = av.GetAvatarShapingComponent()
    for name, w in values.items():
        sc.SetShapingMorphWeight(SLIDERS[name], float(w))
    return slider_weights(av)


def _bones(d):
    return [{"name": k, "p": v} for k, v in d.items()]


def _weights(d):
    return [{"name": k, "weight": v} for k, v in d.items()]


def dump_skeleton(path):
    """Writes {"sliders": [{name, weight}], "bones": [{name, p}]} for the avatar as it is now
    (p: the bind localPosition the Unity export would have)."""
    av = _avatar()
    weights, bones = slider_weights(av), bone_vectors(av)
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"sliders": _weights(weights), "bones": _bones(bones)}, f, indent=1)
    return {"path": path, "bones": len(bones), "sliders": {k: v for k, v in weights.items() if v}}


def probe_sliders(path, step=0.5):
    """For each slider: set +step on top of the current weight, read the skeleton, restore.
    Writes {"step", "sliders": [{name, weight}] (current weights), "bones": [{name, p}] (current bone
    vectors), "delta": [{"slider", "bones": [{name, p}]}] (per unit weight, only bones that moved
    > 1e-5 m per unit)}."""
    av = _avatar()
    sc = av.GetAvatarShapingComponent()
    base_w = slider_weights(av)
    base = bone_vectors(av)
    delta, worst_restore = {}, 0.0
    for name, mid in SLIDERS.items():
        w0 = base_w[name]
        sc.SetShapingMorphWeight(mid, w0 + step)
        moved = bone_vectors(av)
        sc.SetShapingMorphWeight(mid, w0)
        back = bone_vectors(av)
        per = {}
        for bone, v0 in base.items():
            d = [(moved[bone][i] - v0[i]) / step for i in range(3)]
            if max(abs(c) for c in d) > 1e-5:
                per[bone] = d
            worst_restore = max(worst_restore, max(abs(back[bone][i] - v0[i]) for i in range(3)))
        if per:
            delta[name] = per
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"step": step, "sliders": _weights(base_w), "bones": _bones(base),
                   "delta": [{"slider": k, "bones": _bones(v)} for k, v in delta.items()]}, f, indent=1)
    return {"path": path, "moving_sliders": {k: len(v) for k, v in delta.items()},
            "static_sliders": [k for k in SLIDERS if k not in delta],
            "worst_restore_m": worst_restore, "weights_restored": slider_weights(av) == base_w}
