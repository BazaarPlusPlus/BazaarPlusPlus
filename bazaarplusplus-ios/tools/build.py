#!/usr/bin/env python3
"""Build the BazaarPlusPlus App: UnityFramework with static Mono and corlib AOT, then the launcher that embeds it."""
import argparse
import json
from pathlib import Path
import plistlib
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', choices=['simulator', 'device'])
    parser.add_argument('--test-control', action='store_true', help='opt into the local Simulator integration test socket')
    args = parser.parse_args()
    sim = args.target == 'simulator'
    if args.test_control and not sim:
        parser.error('--test-control requires a Simulator build')
    sdk = 'iphonesimulator' if sim else 'iphoneos'
    flavor = 'sim-aot' if sim else 'device'
    source = ROOT / 'runtime/engine/shell/Builds' / ('iOSRelease' if sim else 'iOSDevice')
    out = ROOT / 'build/steam-app' / args.target
    export = out / 'export'
    export.mkdir(parents=True, exist_ok=True)
    # A generated copy keeps the stock IL2CPP export available for comparison.
    subprocess.run(['rsync', '-a', '--delete', str(source) + '/', str(export) + '/'], check=True)
    if not sim:
        prepared = out / 'engine'
        subprocess.run(['python3', str(ROOT / 'runtime/engine/prepare-device.py'),
                        str(source / 'Libraries/libiPhone-lib.a'), str(prepared)], check=True)
        shutil.copy2(prepared / 'libiPhone-lib.a', export / 'Libraries/libiPhone-lib.a')
        shutil.copy2(prepared / 'UnityICallRegistration.cpp',
                     export / 'Il2CppOutputProject/Source/il2cppOutput/UnityICallRegistration.cpp')
    subprocess.run(['python3', str(ROOT / 'middleware/fmod/prepare.py'), args.target], check=True)
    subprocess.run(['dotnet', 'run', '--project', str(ROOT / 'middleware/fmod/adapter'), '-c', 'Release', '--',
                    str(ROOT / 'game/game.json'),
                    str(ROOT / 'build/steam/download/TheBazaar.app/Contents/Resources/Data/Managed/FMODUnity.dll'),
                    str(ROOT / 'build/game-adapters')], check=True)
    subprocess.run(['bash', str(ROOT / 'service/build.sh'), args.target], check=True)
    subprocess.run(['bash', str(ROOT / 'runtime/helper/build.sh'),
                    str(ROOT / 'build/steam/download/TheBazaar.app/Contents/Resources/Data/Managed')], check=True)
    subprocess.run(['python3', str(ROOT / 'tools/stage-mod.py')], check=True)
    subprocess.run(['python3', str(ROOT / 'middleware/sqlite/build.py'), args.target], check=True)
    subprocess.run(['python3', str(ROOT / 'middleware/alembic/build.py'), args.target], check=True)
    subprocess.run(['bash', str(ROOT / 'runtime/mono/build-aot.sh'), args.target], check=True)
    subprocess.run(['bash', str(ROOT / 'runtime/build-static.sh'), args.target], check=True)
    project = export / 'Unity-iPhone.xcodeproj/project.pbxproj'
    doc = json.loads(subprocess.check_output(['plutil', '-convert', 'json', '-o', '-', str(project)]))
    objects = doc['objects']
    framework = next(v for v in objects.values() if v.get('isa') == 'PBXNativeTarget' and v.get('name') == 'UnityFramework')
    game_targets = {k for k,v in objects.items() if v.get('isa') == 'PBXNativeTarget' and v.get('name') == 'GameAssembly'}
    framework['dependencies'] = [k for k in framework.get('dependencies', []) if objects[k].get('target') not in game_targets]
    for phase_id in framework['buildPhases']:
        phase = objects[phase_id]
        phase['files'] = [k for k in phase.get('files', []) if objects.get(objects[k].get('fileRef'), {}).get('path') != 'libGameAssembly.a']
    mono = ROOT / f'build/mono-{flavor}-arm64/lib'
    archives = [ROOT / f'build/shim-static-{args.target}/libshim.a', mono / 'libmonoboehm-2.0.a', mono / 'libmono-native.a', ROOT / f'build/aot-{args.target}/libcorlib-aot.a']
    archives.append(ROOT / f'build/steam-native-{args.target}/libzstd.a')
    archives.append(ROOT / f'build/sqlite-{args.target}/libsqlite.a')
    archives.append(ROOT / f'build/alembic-{args.target}/cmake/libabci.a')
    fmod = json.loads((ROOT / 'toolchain.lock.json').read_text())['fmodRequired']
    archives.extend(ROOT / f'build/fmod-{args.target}' / name for name in
                    fmod['simulatorLibraries' if sim else 'deviceLibraries'])
    if not sim:
        # The device engine archive includes unused editor/encoder routines whose
        # dependencies are absent from the player. Strip those, but retain runtime
        # entry points reached via dlsym rather than ordinary linker references.
        symbols = subprocess.check_output(['xcrun', 'nm', '-gjU', *map(str, archives)], text=True)
        # Mono resolves public C entry points. Compiler block descriptors contain
        # control characters and must not become response-file linker roots.
        roots = sorted({line.strip() for line in symbols.splitlines()
                        if re.fullmatch(r'_[A-Za-z][A-Za-z0-9_]*', line.strip())})
        retained = out / 'runtime-symbols.ldflags'
        retained.write_text(''.join(f'-Wl,-u,{symbol}\n' for symbol in roots))
    for config in objects[framework['buildConfigurationList']]['buildConfigurations']:
        settings = objects[config]['buildSettings']
        flags = settings.get('OTHER_LDFLAGS', [])
        assert isinstance(flags, list)
        flags = [f for f in flags if 'il2cpp.a' not in f]
        for archive in archives:
            flags += ['-force_load', str(archive)]
        for name in ['Security', 'GSS', 'CoreAudio', 'AudioToolbox', 'AVFoundation']:
            flags += ['-framework', name]
        flags += ['-lz', '-liconv', '-lc++', '-Wl,-export_dynamic', '-Wl,-no_deduplicate']
        if not sim:
            flags += ['@' + str(retained)]
        settings['OTHER_LDFLAGS'] = flags
        settings['GCC_SYMBOLS_PRIVATE_EXTERN'] = 'NO'
        settings['DEAD_CODE_STRIPPING'] = 'NO' if sim else 'YES'
    if not sim:
        sources = next(objects[k] for k in framework['buildPhases'] if objects[k]['isa'] == 'PBXSourcesBuildPhase')
        for index, name in enumerate(['UnityClassRegistration.cpp', 'UnityICallRegistration.cpp']):
            ref, build = f'ABCDEF0000000000000000{index}0', f'ABCDEF0000000000000000{index}1'
            objects[ref] = {'isa': 'PBXFileReference', 'lastKnownFileType': 'sourcecode.cpp.cpp', 'path': f'Il2CppOutputProject/Source/il2cppOutput/{name}', 'sourceTree': 'SOURCE_ROOT'}
            objects[build] = {'isa': 'PBXBuildFile', 'fileRef': ref}
            sources['files'].append(build)
    filesystem = export / 'Classes/Unity/Filesystem.mm'
    contents = filesystem.read_text()
    contents = '#include <stdlib.h>\n' + contents.replace('    static const char* dir = NULL;', '    const char* override = getenv("BPP_GAME_ROOT");\n    if (override && *override) return override;\n    static const char* dir = NULL;')
    filesystem.write_text(contents)
    project.write_bytes(plistlib.dumps(doc))
    managed = export / 'Data/Managed'
    managed.mkdir(exist_ok=True)
    sample = ROOT / 'runtime/engine/shell/Builds/MacRGBA/BppShell.app/Contents/Resources/Data'
    for dll in (sample / 'Managed').glob('*.dll'):
        shutil.copy2(dll, managed / dll.name)
    version = json.loads((ROOT / 'toolchain.lock.json').read_text())['unity']['version']
    engine = Path(f'/Applications/Unity/Hub/Editor/{version}/PlaybackEngines/iOSSupport/Variations/il2cpp/Development{"_simarm64" if sim else "arm64"}_managed')
    assert (engine / 'UnityEngine.CoreModule.dll').exists(), engine
    for dll in engine.glob('UnityEngine*.dll'):
        shutil.copy2(dll, managed / dll.name)
    # The game brings its own assembly lists; the shell's must not reach the App.
    for name in ['ScriptingAssemblies.json', 'RuntimeInitializeOnLoads.json']:
        (export / 'Data' / name).unlink(missing_ok=True)
    (managed / 'Assembly-CSharp.dll').unlink(missing_ok=True)
    for dll in (ROOT / 'service/Managed/bin/Release/netstandard2.0').glob('*.dll'):
        shutil.copy2(dll, managed / dll.name)
    log = ROOT / f'build/logs/steam-app-{args.target}.log'
    with log.open('w') as output:
        # The Unity export builds only the runtime; the launcher project owns the App.
        subprocess.run(['xcodebuild', '-project', str(project.parent), '-scheme', 'UnityFramework',
                        '-configuration', 'Release', '-sdk', sdk,
                        '-destination', 'generic/platform=' + ('iOS Simulator' if sim else 'iOS'),
                        '-derivedDataPath', str(out / 'derived'),
                        'ARCHS=arm64', 'CODE_SIGNING_ALLOWED=NO', 'build'],
                       stdout=output, stderr=subprocess.STDOUT, check=True)
        subprocess.run(['python3', str(ROOT / 'tools/generate-xcodeproj.py')], check=True)
        signing = [] if sim else ['CODE_SIGNING_ALLOWED=NO']
        subprocess.run(['xcodebuild', '-project', str(ROOT / 'app/BazaarPlusPlus.xcodeproj'),
                        '-scheme', 'BazaarPlusPlus', '-configuration', 'Release', '-sdk', sdk,
                        '-destination', 'generic/platform=' + ('iOS Simulator' if sim else 'iOS'),
                        '-derivedDataPath', str(out / 'launcher-derived'), 'ARCHS=arm64',
                        'BPP_STEAM_TEST_CONTROL=' + ('1' if args.test_control else '0'),
                        *signing, 'build'], stdout=output, stderr=subprocess.STDOUT, check=True)
    app = out / f'launcher-derived/Build/Products/Release-{sdk}/BazaarPlusPlus.app'
    print(app)

if __name__ == '__main__':
    main()
