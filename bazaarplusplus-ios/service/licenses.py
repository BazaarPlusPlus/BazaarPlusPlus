#!/usr/bin/env python3
"""Bundle notices from the exact restored NuGet packages and pinned zstd source."""
import json
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]

def bundle(app):
    destination = app / 'ThirdPartyNotices'
    destination.mkdir(parents=True, exist_ok=True)
    assets = json.loads((ROOT / 'service/Managed/obj/project.assets.json').read_text())
    for name, package in assets['libraries'].items():
        if package['type'] != 'package':
            continue
        source = next(Path(base) / package['path'] for base in assets['packageFolders']
                      if (Path(base) / package['path']).is_dir())
        target = destination / name.replace('/', '-')
        target.mkdir(exist_ok=True)
        spec = next(source.glob('*.nuspec'))
        shutil.copy2(spec, target / spec.name)
        notices = [p for p in source.iterdir() if p.is_file() and
                   any(word in p.name.lower() for word in ('licen', 'notice', 'party'))]
        if not notices:
            license = ET.parse(spec).find('.//{*}license')
            fallback = ROOT / 'service/Managed/licenses' / ((license.text if license is not None else '') + '.txt')
            if not fallback.is_file():
                raise RuntimeError('Missing license text for ' + name)
            notices = [fallback]
        for notice in notices:
            shutil.copy2(notice, target / notice.name)
    lock = json.loads((ROOT / 'toolchain.lock.json').read_text())['zstd']
    shutil.copy2(ROOT / ('build/vendor/zstd-' + lock['version'] + '/LICENSE'), destination / 'zstd-LICENSE')
    shutil.copy2(ROOT / 'service/Managed/THIRD_PARTY.md', destination / 'Steam-protocol.md')
    shutil.copy2(ROOT / 'service/Managed/AuthTickets.cs', destination / 'AuthTickets.cs')
    shutil.copy2(ROOT / 'service/Native/chunks.c', destination / 'chunks.c')
    dependencies = json.loads((ROOT / 'toolchain.lock.json').read_text())
    fmod = ROOT / ('build/vendor/fmod-' + dependencies['fmodRequired']['version'])
    shutil.copy2(fmod / 'LICENSE.txt', destination / 'FMOD-LICENSE.txt')
    shutil.copy2(ROOT / 'service/Managed/licenses/Apache-2.0.txt', destination / 'ResonanceAudio-Apache-2.0.txt')
    (destination / 'FMOD-attribution.txt').write_text('FMOD by Firelight Technologies Pty Ltd.\n')
    sqlite = dependencies['sqlite']
    text = (ROOT / 'build/vendor' / sqlite['folder'] / 'sqlite3.c').read_text()
    (destination / 'SQLite-notice.txt').write_text(text[:text.index('*/') + 2] + '\n')
    for name, source in dependencies['alembicNative'].items():
        root = ROOT / 'build/vendor' / source['folder']
        notices = [p for p in root.rglob('*') if p.is_file() and
                   p.name.lower() in ('license', 'license.txt', 'license.md', 'copyright', 'copying')]
        if not notices:
            raise RuntimeError('Missing native dependency notices: ' + name)
        for i, notice in enumerate(sorted(notices)):
            shutil.copy2(notice, destination / f'{name}-{i}-{notice.name}')

if __name__ == '__main__':
    bundle(Path(sys.argv[1]))
