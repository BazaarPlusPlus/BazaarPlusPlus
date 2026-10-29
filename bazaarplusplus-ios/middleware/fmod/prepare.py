#!/usr/bin/env python3
"""Import the licensed, exact-version Unity package; never fetch credentials or retarget binaries."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import tarfile

ROOT = Path(__file__).resolve().parents[2]

def digest(data):
    return hashlib.sha256(data).hexdigest()

def prepare(target, package=None):
    lock = json.loads((ROOT / 'toolchain.lock.json').read_text())['fmodRequired']
    receipt = lock['unityPackage']
    package = package or ROOT / 'build/vendor' / receipt['filename']
    data = package.read_bytes()
    if digest(data) != receipt['sha256']:
        raise ValueError('FMOD Unity package hash differs from pinned version')
    vendor = ROOT / ('build/vendor/fmod-' + lock['version'])
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as archive:
        # Read once sequentially; Unity packages store the original name separately.
        contents = {m.name: archive.extractfile(m).read() for m in archive if m.isfile()}
    found = {}
    for name, value in contents.items():
        if not name.endswith('/pathname'):
            continue
        path = value.decode().removeprefix('Assets/Plugins/FMOD/')
        if path not in receipt['files']:
            continue
        blob = contents[name.rsplit('/', 1)[0] + '/asset']
        if digest(blob) != receipt['files'][path]:
            raise ValueError('FMOD package member mismatch: ' + path)
        found[path] = blob
    if set(found) != set(receipt['files']):
        raise ValueError('FMOD package members missing')
    for path, blob in found.items():
        out = vendor / path
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(blob)
    if f'0x{lock["versionNumber"]:08x}' not in found['src/fmod.cs'].decode().lower():
        raise ValueError('FMOD binding version mismatch')
    out = ROOT / f'build/fmod-{target}'
    out.mkdir(parents=True, exist_ok=True)
    libraries = lock['simulatorLibraries' if target == 'simulator' else 'deviceLibraries']
    evidence = []
    for name in libraries:
        src, thin = vendor / 'platforms/ios/lib' / name, out / name
        subprocess.run(['xcrun', 'lipo', str(src), '-thin', 'arm64', '-output', str(thin)], check=True)
        load = subprocess.check_output(['xcrun', 'otool', '-l', str(thin)], text=True)
        if 'is an LLVM bit-code file' in load:
            triples = re.findall(rb'arm64-apple-ios\d+\.\d+\.\d+(?:-simulator)?', thin.read_bytes())
            if not triples or any((b'-simulator' in t) != (target == 'simulator') for t in triples):
                raise ValueError('Resonance bitcode platform mismatch')
            platforms = sorted({t.decode() for t in triples})
        else:
            platforms = re.findall(r'platform\s+(\d+)', load)
            if not platforms or set(platforms) != {'7' if target == 'simulator' else '2'}:
                raise ValueError('FMOD Mach-O platform mismatch')
        evidence.append({'file': name, 'sha256': digest(thin.read_bytes()), 'platforms': platforms})
    (out / 'receipt.json').write_text(json.dumps({'version': lock['version'], 'packageSha256': receipt['sha256'],
                                               'target': target, 'libraries': evidence}, indent=2) + '\n')
    print(f'FMOD {lock["version"]}: verified {target} arm64 libraries')
    return [out / name for name in libraries]

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', choices=['device', 'simulator'])
    parser.add_argument('--package', type=Path)
    args = parser.parse_args()
    prepare(args.target, args.package)
