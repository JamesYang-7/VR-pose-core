#!/usr/bin/env python3
"""Write Models/model_fingerprint.txt: the SHA-256 of every model file git doesn't hold.

The model exports under Models/ (e.g. rpm/) keep their graphs (models/<name>/*.onnx), the
GORP captures (data/) and each version's reference output (models/<name>/output_ref/) out of git;
they travel in the projects' local-files zip, so git alone can't say which models
a commit expects. This file can: it is committed, and ModelFingerprintTests fails when the local
files don't match it. The file list is what git ignores under Models/ (.meta files left
out), so a new export is picked up without editing this script.

Run it after installing a new export, then commit the file and repack the zip. Output is sorted
and deterministic.

Usage: python Tools~/model_fingerprint.py
Standard library only.
"""
import hashlib
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS_DIR = os.path.join(ROOT, "Models")
OUT = os.path.join(MODELS_DIR, "model_fingerprint.txt")

HEADER = """\
# SHA-256 of the model files under Models/ that are not in git (they travel in the local-files zip).
# ModelFingerprintTests checks the local files against this list.
# After installing a new export: python Tools~/model_fingerprint.py, then commit.
"""


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def model_files():
    out = subprocess.run(["git", "ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--", "Models"],
                         cwd=ROOT, check=True, capture_output=True).stdout.decode("utf-8")
    prefix = "Models/"
    return sorted(p[len(prefix):] for p in out.split("\0") if p.startswith(prefix) and not p.endswith(".meta"))


def main():
    files = model_files()
    if not files:
        sys.exit("No ignored model files under Models/. Put the model files in place first.")
    lines = [f"{sha256(os.path.join(MODELS_DIR, f))}  {f}" for f in files]
    with open(OUT, "w", encoding="utf-8", newline="\n") as out:
        out.write(HEADER + "\n".join(lines) + "\n")
    print(f"Wrote {os.path.relpath(OUT, ROOT)} ({len(lines)} files).")


if __name__ == "__main__":
    main()
