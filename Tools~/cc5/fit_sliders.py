"""Fits CC5 body-slider values so a retargeted character follows the SMPL-X body.

Inputs come from Unity (`RetargetFit.WriteSliderResiduals`, run on a probe from
`Tools~/cc5/cc5_skeleton.py`): `residuals.npy` (1 + sliders, frames, columns) — per-frame target − source
errors in metres for the probe's base skeleton (row 0) and for the base plus `step` of each slider — and
`fit_meta.json`. Joint positions are linear in the bone vectors and the bone vectors are linear in the
sliders (CC5: to 1e-7 m), so the residual is linear in the slider weights too, up to the arm aims
turning by < 1° per slider. The prediction is to be checked by setting the sliders in CC5 and
evaluating the measured skeleton (`RetargetFit.EvaluateSkeleton`).

The fit is a bounded linear least squares on, in cm²:
  data = mean over clip groups of the frame-mean of  wrist·|Δwrist|² + sole·Δsole² + elbow·|Δelbow|²
  + anatomy · Σ_bones |change of the bone vector|²      (the main limb and spine bones, from the probe)
The anatomy term keeps Aaron's proportions unless a change pays for itself: without it the fit trades
e.g. 9 cm of thigh for 8 cm of shin to fit the feet in a few sitting frames.

    python Tools~/cc5/fit_sliders.py CC5/fit CC5/aaron_sliders.json              # writes CC5/fit/fit.json
    python Tools~/cc5/fit_sliders.py CC5/fit CC5/aaron_sliders.json --sole 0 --tag wrists_only

Needs numpy and scipy.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy.optimize import lsq_linear

# Sliders the fit may use. Left out: Head Scale (changes the face), Neck Length (would compensate
# head-joint placement, not anatomy — the eye anchor already handles that), Shoulder Scale (moves no
# joint).
ALLOWED = [
    "Character Scale A", "Arm Length", "Upper Arm Length", "Forearm Length", "Shoulder Width A",
    "Chest Scale", "Chest Width", "Chest Height", "Hip Length A", "Hip Width A",
    "Leg Length", "Thigh Length", "Lower Leg Length", "Foot Length",
]

# Bones whose change counts as anatomy (each bone vector is the offset from its parent joint). Twist and
# share bones are left out: they only split a limb segment the main bone already counts.
ANATOMY_BONES = ["Waist", "Spine01", "Spine02", "NeckTwist01", "NeckTwist02", "Head"] + [
    f"{s}_{b}" for s in "LR" for b in ["Thigh", "Calf", "Foot", "ToeBase", "Clavicle", "Upperarm", "Forearm", "Hand"]]

WRIST = slice(0, 6)
SOLE = slice(6, 8)
ELBOW = slice(8, 14)


def load(fit_dir, probe_path):
    fit_dir = Path(fit_dir)
    res = np.load(fit_dir / "residuals.npy").astype(np.float64) * 100.0  # cm
    meta = json.loads((fit_dir / "fit_meta.json").read_text())
    base = res[0]
    jac = (res[1:] - base[None]) / meta["step"]  # (sliders, frames, columns), cm per unit weight

    probe = json.loads(Path(probe_path).read_text())
    by_slider = {d["slider"]: {b["name"]: b["p"] for b in d["bones"]} for d in probe["delta"]}
    names = [f"CC_Base_{b}" for b in ANATOMY_BONES]
    bones = np.array([[by_slider[s].get(n, [0.0, 0.0, 0.0]) for n in names] for s in meta["sliders"]]) * 100.0
    return base, jac, bones.reshape(len(meta["sliders"]), -1), meta  # bones: cm per unit weight


def metrics(r, groups, names):
    """Per group and overall: wrist / elbow norms, sole signed + abs, in cm."""
    out = {}
    for g in [-1] + sorted(set(groups.tolist())):
        x = r if g < 0 else r[groups == g]
        wrist = np.concatenate([np.linalg.norm(x[:, 0:3], axis=1), np.linalg.norm(x[:, 3:6], axis=1)])
        elbow = np.concatenate([np.linalg.norm(x[:, 8:11], axis=1), np.linalg.norm(x[:, 11:14], axis=1)])
        sole = x[:, SOLE].ravel()
        out["all" if g < 0 else names[g]] = {
            "wrist": wrist.mean(), "wrist_p95": np.percentile(wrist, 95), "elbow": elbow.mean(),
            "sole_signed": sole.mean(), "sole_abs": np.abs(sole).mean(), "sole_p95": np.percentile(np.abs(sole), 95),
            "sole_below_2cm": float(np.mean(sole < -2.0)) * 100,
        }
    return out


def print_metrics(title, m):
    print(title)
    for g, v in m.items():
        print(f"  {g:9s} wrist {v['wrist']:4.1f} / p95 {v['wrist_p95']:4.1f} | elbow {v['elbow']:4.1f} | "
              f"sole {v['sole_signed']:+5.1f} signed, {v['sole_abs']:4.1f} / p95 {v['sole_p95']:4.1f} abs, "
              f"below by >2 cm {v['sole_below_2cm']:3.0f}%")


def fit(base, jac, bones, meta, w_wrist, w_sole, w_elbow, anatomy, slider, allowed, bound):
    sliders = meta["sliders"]
    groups = np.asarray(meta["frame_group"])
    use = [i for i, s in enumerate(sliders) if s in allowed]

    # Each clip group counts equally, whatever its frame count.
    n_groups = len(set(groups.tolist()))
    gw = np.zeros(len(groups))
    for g in set(groups.tolist()):
        gw[groups == g] = 1.0 / np.sum(groups == g) / n_groups
    cw = np.zeros(base.shape[1])
    cw[WRIST], cw[SOLE], cw[ELBOW] = w_wrist, w_sole, w_elbow
    w = np.sqrt(gw[:, None] * cw[None, :]).ravel()

    a = np.vstack([(jac[use].reshape(len(use), -1) * w[None, :]).T,
                   np.sqrt(anatomy) * bones[use].T,
                   np.sqrt(slider) * np.eye(len(use))])
    b = np.concatenate([-(base.ravel() * w), np.zeros(bones.shape[1] + len(use))])
    sol = lsq_linear(a, b, bounds=(-bound, bound))
    x = np.zeros(len(sliders))
    x[use] = sol.x
    return x, base + np.tensordot(x, jac, axes=1)


def bone_changes(x, bones):
    """Largest bone-vector changes, cm."""
    d = (x @ bones).reshape(-1, 3)
    mags = np.linalg.norm(d, axis=1)
    order = np.argsort(-mags)
    return [(ANATOMY_BONES[i], mags[i]) for i in order if mags[i] > 0.2]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("fit_dir")
    ap.add_argument("probe", help="slider probe from cc5_skeleton.probe_sliders (for the anatomy term)")
    ap.add_argument("--wrist", type=float, default=1.0, help="weight of the wrist errors")
    ap.add_argument("--sole", type=float, default=1.0, help="weight of the foot-sole height errors")
    ap.add_argument("--elbow", type=float, default=0.2, help="weight of the elbow errors")
    ap.add_argument("--anatomy", type=float, default=0.1, help="weight of the bone changes (per cm²)")
    ap.add_argument("--slider", type=float, default=1.0, help="weight of the slider changes (per unit²): picks the smallest of slider combinations with the same effect (Leg Length ≈ Thigh + Lower Leg Length)")
    ap.add_argument("--bound", type=float, default=1.0, help="|weight| limit per slider")
    ap.add_argument("--exclude", nargs="*", default=[], help="sliders to leave at their current value")
    ap.add_argument("--tag", default="fit", help="output name: <fit_dir>/<tag>.json")
    args = ap.parse_args()

    base, jac, bones, meta = load(args.fit_dir, args.probe)
    groups = np.asarray(meta["frame_group"])
    allowed = [s for s in ALLOWED if s not in args.exclude]
    x, pred = fit(base, jac, bones, meta, args.wrist, args.sole, args.elbow, args.anatomy, args.slider, allowed, args.bound)

    print_metrics("current skeleton (cm):", metrics(base, groups, meta["groups"]))
    print_metrics("fitted (predicted, cm):", metrics(pred, groups, meta["groups"]))
    print("slider changes:", ", ".join(f"{s} {v:+.3f}" for s, v in sorted(zip(meta["sliders"], x), key=lambda t: -abs(t[1])) if abs(v) > 5e-3))
    print("bone changes (cm):", ", ".join(f"{n} {m:.1f}" for n, m in bone_changes(x, bones)))
    out = Path(args.fit_dir) / f"{args.tag}.json"
    out.write_text(json.dumps({"weights": vars(args),
                               "sliders": {s: round(float(v), 3) for s, v in zip(meta["sliders"], x) if abs(v) > 5e-3},
                               "predicted": metrics(pred, groups, meta["groups"])}, indent=1))
    print(f"-> {out}")


if __name__ == "__main__":
    main()
