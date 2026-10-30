#!/usr/bin/env python3
"""Zip the signed device build into a sideloadable IPA named after the Product Release and game build."""
from pathlib import Path
import plistlib
import shutil
import tempfile

ROOT = Path(__file__).resolve().parents[1]
app = ROOT / 'build/steam-app/device/launcher-derived/Build/Products/Release-iphoneos/BazaarPlusPlus.app'
if not app.exists():
    raise SystemExit('Build the device App first: python3 tools/build.py device')
info = plistlib.loads((app / 'Info.plist').read_bytes())
game = info['BPPGameVersion'].split('-')[0]
name = f"BazaarPlusPlus_{info['CFBundleShortVersionString']}_ios-aarch64_game{game}"
with tempfile.TemporaryDirectory() as staging:
    payload = Path(staging) / 'Payload'
    payload.mkdir()
    shutil.copytree(app, payload / app.name, symlinks=True)
    archive = shutil.make_archive(str(ROOT / 'build' / name), 'zip', staging)
ipa = Path(archive).with_suffix('.ipa')
Path(archive).replace(ipa)
print(ipa)
