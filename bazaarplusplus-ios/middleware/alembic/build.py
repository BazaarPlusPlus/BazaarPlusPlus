#!/usr/bin/env python3
"""Cross-build Unity's Alembic plugin and pinned Ogawa/IlmBase dependencies, without desktop ISPC."""
import argparse, hashlib, json, os, subprocess, tarfile, urllib.request
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def fetch(name,lock):
    vendor=ROOT/'build/vendor';vendor.mkdir(parents=True,exist_ok=True)
    archive=vendor/(name+'-'+lock['revision']+'.tar.gz')
    if not archive.exists():
        with urllib.request.urlopen(lock['url'],timeout=120) as r:archive.write_bytes(r.read())
    if hashlib.sha256(archive.read_bytes()).hexdigest()!=lock['sha256']:raise ValueError('Dependency checksum mismatch: '+name)
    source=vendor/lock['folder']
    if not source.exists():
        with tarfile.open(archive) as tar:
            for member in tar.getmembers():
                path=Path(member.name)
                if path.is_absolute() or '..' in path.parts or member.issym() or member.islnk():raise ValueError('Unsafe archive member')
            tar.extractall(vendor)
    return source

def build(target):
    lock=json.loads((ROOT/'toolchain.lock.json').read_text())['alembicNative']
    sources={k:fetch(k,v) for k,v in lock.items()};ilm=sources['openexr']/'IlmBase'
    output=ROOT/f'build/alembic-{target}';generated=output/'generated';generated.mkdir(parents=True,exist_ok=True)
    # These are build-time table generators, compiled for the Mac, never run as iOS executables.
    for name in ['eLut','toFloat']:
        exe=output/name
        subprocess.run(['xcrun','--sdk','macosx','clang++','-std=c++14',str(ilm/'Half'/f'{name}.cpp'),'-o',str(exe)],check=True)
        with (generated/f'{name}.h').open('w') as out:subprocess.run([str(exe)],stdout=out,check=True)
    (generated/'IlmBaseConfig.h').write_text('#define HAVE_PTHREAD 1\n#define ILMBASE_INTERNAL_NAMESPACE_CUSTOM 0\n#define IMATH_NAMESPACE Imath\n#define IEX_NAMESPACE Iex\n#define ILMTHREAD_NAMESPACE IlmThread\n')
    config=generated/'Alembic/Util/Config.h';config.parent.mkdir(parents=True,exist_ok=True);config.write_text('#pragma once\n#define ALEMBIC_LIBRARY_VERSION 10805\n')
    sdk={'device':'iphoneos','simulator':'iphonesimulator','host':'macosx'}[target]
    flags=['-DCMAKE_BUILD_TYPE=Release','-DCMAKE_OSX_ARCHITECTURES=arm64','-DCMAKE_OSX_SYSROOT='+subprocess.check_output(['xcrun','--sdk',sdk,'--show-sdk-path'],text=True).strip()]
    if target!='host':flags+=['-DCMAKE_SYSTEM_NAME=iOS','-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0','-DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY']
    subprocess.run(['cmake','-S',str(ROOT/'middleware/alembic'),'-B',str(output/'cmake'),*flags,'-DILM='+str(ilm),'-DABC='+str(sources['alembic']),'-DPLUGIN='+str(sources['unityAlembic']/'Source/abci'),'-DGENERATED='+str(generated)],check=True)
    subprocess.run(['cmake','--build',str(output/'cmake'),'--parallel','8'],check=True)
    return output/'cmake/libabci.a'
if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('target',choices=['simulator','device','host']);print(build(p.parse_args().target))
