#!/usr/bin/env python3
"""Build the Audio2Face native plugin and install a model into a Unity project that uses this package.

The plugin (Native~/Audio2FacePlugin/) wraps the NVIDIA Audio2X SDK in a C API that
VrChat.Audio2Face calls through P/Invoke. Both commands need a local clone of
https://github.com/NVIDIA/Audio2Face-3D-SDK that has been built (build.bat) and has
had its models downloaded and converted (download_models.bat, gen_testdata.bat).

  build          configure + build the plugin with Visual Studio 2022 and copy
                 a2f_unity.dll and audio2x.dll into Assets/Plugins/x86_64/Audio2Face/.
                 --test also runs the native test program on the SDK's sample clip.
  install-model  copy a regression model (the files its model.json names) into
                 Assets/StreamingAssets/Audio2Face/<character>/, where
                 Audio2FaceDriver.ModelPath looks for it. Its network.trt is a
                 TensorRT engine built for one GPU and TensorRT version: on another
                 machine, run gen_testdata.bat in that machine's SDK clone first.

Both write into the Unity project around this package (Packages/<name>/ inside it);
--project names another one. Close the Unity editor before `build` if it has loaded
the plugin (Play mode or the Audio2Face tests): Windows keeps a loaded DLL locked
until the editor exits.

Usage:
  python Tools~/audio2face_plugin.py build --sdk <sdk clone> [--test] [--project <Unity project>]
  python Tools~/audio2face_plugin.py install-model --sdk <sdk clone> [--character james] [--project <Unity project>]
--sdk defaults to the AUDIO2X_SDK_ROOT environment variable. Needs CMake on PATH.
Standard library only.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

PACKAGE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(PACKAGE, "Native~", "Audio2FacePlugin")
BUILD = os.path.join(SOURCE, "build")


def project_root(args):
    """The Unity project to install into: --project, else the one this package sits in."""
    if args.project:
        return os.path.abspath(args.project)
    path = PACKAGE
    while os.path.dirname(path) != path:
        path = os.path.dirname(path)
        if os.path.isdir(os.path.join(path, "Assets")) and os.path.isdir(os.path.join(path, "ProjectSettings")):
            return path
    sys.exit("This package is not inside a Unity project: pass --project <Unity project>.")


def sdk_root(args):
    root = args.sdk or os.environ.get("AUDIO2X_SDK_ROOT")
    if not root:
        sys.exit("Pass --sdk <Audio2Face-3D-SDK clone> or set AUDIO2X_SDK_ROOT.")
    return os.path.abspath(root)


def run(cmd):
    print("+", " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True)


def build(args):
    root = sdk_root(args)
    project = project_root(args)
    plugin_dir = os.path.join(project, "Assets", "Plugins", "x86_64", "Audio2Face")
    sdk = os.path.join(root, "_build", "release", "audio2x-sdk")
    if not os.path.isfile(os.path.join(sdk, "lib", "audio2x.lib")):
        sys.exit(f"{sdk} has no lib/audio2x.lib: build the SDK first (build.bat all release).")
    run(["cmake", "-S", SOURCE, "-B", BUILD, "-G", "Visual Studio 17 2022", "-A", "x64",
         f"-DAUDIO2X_SDK_DIR={sdk}", f"-DA2F_UNITY_PLUGIN_DIR={plugin_dir}"])
    run(["cmake", "--build", BUILD, "--config", "Release"])
    run(["cmake", "--install", BUILD, "--config", "Release"])
    print(f"Installed a2f_unity.dll and audio2x.dll into {plugin_dir}")
    if args.test:
        model = os.path.join(root, "_data", "generated", "audio2face-sdk", "samples", "data", "james", "model.json")
        clip = os.path.join(root, "sample-data", "audio_4sec_16k_s16le.wav")
        run([os.path.join(BUILD, "Release", "a2f_unity_test.exe"), model, clip, "--realtime"])


def install_model(args):
    root = sdk_root(args)
    project = project_root(args)
    source = os.path.join(root, "_data", "generated", "audio2face-sdk", "samples", "data", args.character)
    manifest = os.path.join(source, "model.json")
    if not os.path.isfile(manifest):
        sys.exit(f"{manifest} not found: run download_models.bat and gen_testdata.bat in the SDK clone.")
    with open(manifest, encoding="utf-8") as f:
        model = json.load(f)
    files = {"model.json"}
    for key, value in model.items():
        if key == "blendshapePaths":
            for part in value.values():
                files.update(part.values())
        elif isinstance(value, str):
            files.add(value)
    target = os.path.join(project, "Assets", "StreamingAssets", "Audio2Face", args.character)
    os.makedirs(target, exist_ok=True)
    total = 0
    for name in sorted(files):
        shutil.copy2(os.path.join(source, name), os.path.join(target, name))
        total += os.path.getsize(os.path.join(target, name))
        print(f"  {name}")
    print(f"Copied {len(files)} files ({total / 1e6:.0f} MB) to {target}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    b = sub.add_parser("build", help="build and install the native plugin")
    b.add_argument("--sdk", help="Audio2Face-3D-SDK clone (default: $AUDIO2X_SDK_ROOT)")
    b.add_argument("--test", action="store_true", help="run the native test program afterwards")
    b.add_argument("--project", help="Unity project to install into (default: the one around this package)")
    b.set_defaults(func=build)
    m = sub.add_parser("install-model", help="copy a model into StreamingAssets")
    m.add_argument("--sdk", help="Audio2Face-3D-SDK clone (default: $AUDIO2X_SDK_ROOT)")
    m.add_argument("--character", default="james", choices=["james", "claire", "mark"])
    m.add_argument("--project", help="Unity project to install into (default: the one around this package)")
    m.set_defaults(func=install_model)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
