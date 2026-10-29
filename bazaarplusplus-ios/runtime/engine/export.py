#!/usr/bin/env python3
"""Export the Unity shell (runtime/engine/shell) for the simulator, the device, or the Mac player whose assemblies the App uses."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=["simulator", "device", "mac"])
    args = parser.parse_args()
    version = json.loads((ROOT / "toolchain.lock.json").read_text())["unity"]["version"]
    editor = Path(os.environ.get("UNITY_EDITOR", f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"))
    project = ROOT / "runtime/engine/shell"
    logs = ROOT / "build/logs"
    logs.mkdir(parents=True, exist_ok=True)
    method, folder = {
        "simulator": ("BuildIOSRelease", "iOSRelease"),
        "device": ("BuildIOSDeviceRelease", "iOSDevice"),
        "mac": ("BuildMacRgba", "MacRGBA"),
    }[args.target]
    log = logs / f"unity-{args.target}.log"
    print(f"Exporting {args.target}; log: {log}", flush=True)
    subprocess.run([str(editor), "-batchmode", "-quit", "-projectPath", str(project),
                    "-buildTarget", "StandaloneOSX" if args.target == "mac" else "iOS",
                    "-executeMethod", f"ShellBuild.{method}", "-logFile", str(log)],
                   check=True, timeout=1200, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    print(f"Exported {project / 'Builds' / folder}")


if __name__ == "__main__":
    try:
        main()
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"Export failed: {error}. See build/logs for diagnostics.", file=sys.stderr)
        sys.exit(1)
