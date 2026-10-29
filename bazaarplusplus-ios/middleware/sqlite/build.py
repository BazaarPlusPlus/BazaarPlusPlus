#!/usr/bin/env python3
"""Build the game's pinned SQLite version as native code for the selected Apple target."""
import argparse, hashlib, json, subprocess, urllib.request, zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def build(target):
    lock=json.loads((ROOT/'toolchain.lock.json').read_text())['sqlite']
    archive=ROOT/'build/vendor'/Path(lock['url']).name; archive.parent.mkdir(parents=True,exist_ok=True)
    if not archive.exists():
        with urllib.request.urlopen(lock['url'],timeout=60) as response: archive.write_bytes(response.read())
    if hashlib.sha256(archive.read_bytes()).hexdigest()!=lock['sha256']: raise ValueError('SQLite source checksum mismatch')
    source=archive.parent/lock['folder']; source.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive) as zip:
        for name in ['sqlite3.c','sqlite3.h']: (source/name).write_bytes(zip.read(lock['folder']+'/'+name))
    sdk,triple={'simulator':('iphonesimulator','arm64-apple-ios15.0-simulator'), 'device':('iphoneos','arm64-apple-ios15.0'), 'host':('macosx','arm64-apple-macos11.0')}[target]
    output=ROOT/f'build/sqlite-{target}';output.mkdir(exist_ok=True)
    subprocess.run(['xcrun','clang','-target',triple,'-isysroot',subprocess.check_output(['xcrun','--sdk',sdk,'--show-sdk-path'],text=True).strip(),'-O2','-fPIC',*['-D'+d for d in lock['defines']],'-c',str(source/'sqlite3.c'),'-o',str(output/'sqlite3.o')],check=True)
    subprocess.run(['xcrun','ar','rcs',str(output/'libsqlite.a'),str(output/'sqlite3.o')],check=True)
    return output/'libsqlite.a'
if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('target',choices=['simulator','device','host']);print(build(p.parse_args().target))
