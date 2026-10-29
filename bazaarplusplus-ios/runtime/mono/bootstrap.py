#!/usr/bin/env python3
"""Fetch the pinned Mono runtime and apply our patch series, or verify it with --check."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
LOCK = json.loads((ROOT / "toolchain.lock.json").read_text())
MONO = ROOT / "third_party/mono"
PATCHES = ROOT / "runtime/mono/patches"


def git(*args, cwd=MONO, **kwargs):
    return subprocess.check_output(["git", *args], cwd=cwd, **kwargs)


def patched_files():
    """Reconstruct expected files from the upstream commit, independent of the working tree."""
    names = (PATCHES / "series").read_text().splitlines()
    files = set()
    added = set()
    for name in names:
        previous = ""
        for line in (PATCHES / name).read_text().splitlines():
            if line.startswith("+++ b/"):
                path = line[6:]
                files.add(path)
                if previous == "--- /dev/null":
                    added.add(path)
            previous = line
    with tempfile.TemporaryDirectory(prefix="bpp-patches-") as directory:
        temp = Path(directory)
        subprocess.run(["git", "init", "-q", str(temp)], check=True)
        for name in files - added:
            path = temp / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(git("show", f"{LOCK['mono']['commit']}:{name}"))
            mode = git("ls-tree", LOCK["mono"]["commit"], "--", name).decode().split()[0]
            if mode == "100755":
                path.chmod(0o755)
        for name in names:
            subprocess.run(["git", "apply", "--check", str(PATCHES / name)], cwd=temp, check=True)
            subprocess.run(["git", "apply", str(PATCHES / name)], cwd=temp, check=True)
        return {name: (temp / name).read_bytes() for name in sorted(files)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify only; do not fetch or modify files")
    args = parser.parse_args()
    if not MONO.exists():
        if args.check:
            parser.error("Mono is missing; run runtime/mono/bootstrap.py first")
        MONO.mkdir(parents=True)
        subprocess.run(["git", "init", "-q", str(MONO)], check=True)
        git("remote", "add", "origin", LOCK["mono"]["url"])
        git("fetch", "--depth", "1", "origin", LOCK["mono"]["commit"])
        git("checkout", "--detach", "FETCH_HEAD")
    if git("rev-parse", "HEAD").decode().strip() != LOCK["mono"]["commit"]:
        parser.error("Mono HEAD differs from toolchain.lock.json; no automatic reset is performed")
    for path, commit in LOCK["mono"]["submodules"].items():
        if not (MONO / path / ".git").exists():
            if args.check:
                parser.error(f"missing Mono submodule: {path}")
            git("submodule", "update", "--init", "--depth", "1", "--", path)
        if git("rev-parse", "HEAD", cwd=MONO / path).decode().strip() != commit:
            parser.error(f"wrong Mono submodule revision: {path}")
        if git("status", "--porcelain", cwd=MONO / path).strip():
            parser.error(f"local modifications in runtime dependency: {path}")
    expected = patched_files()
    differs = [name for name, data in expected.items()
               if not (MONO / name).exists() or (MONO / name).read_bytes() != data]
    if differs and not args.check:
        # Whole-series preflight prevents partially applying over an existing modified checkout.
        patches = [str(PATCHES / name) for name in (PATCHES / "series").read_text().splitlines()]
        subprocess.run(["git", "apply", "--check", *patches], cwd=MONO, check=True)
        subprocess.run(["git", "apply", *patches], cwd=MONO, check=True)
        differs = [name for name, data in expected.items() if (MONO / name).read_bytes() != data]
    if differs:
        parser.error("patch mismatch: " + ", ".join(differs))
    digest = hashlib.sha256((ROOT / "third_party/bcdec/bcdec.h").read_bytes()).hexdigest()
    if digest != LOCK["bcdec"]["sha256"]:
        parser.error("vendored bcdec hash differs from the lock file")
    if not args.check:
        source = (Path("/Applications/Unity/Hub/Editor") / LOCK["unity"]["version"] /
                  "Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/etc/mono")
        if not (ROOT / "build/mono-etc/mono/config").exists():
            shutil.copytree(source, ROOT / "build/mono-etc/mono", dirs_exist_ok=True)
    print(f"Verified {len(expected)} patched Mono files, pinned runtime dependencies and bcdec.")


if __name__ == "__main__":
    main()
