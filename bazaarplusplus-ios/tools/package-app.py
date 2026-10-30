#!/usr/bin/env python3
"""Package launcher resources before signing; only Simulator gets retargeted Mac adapters."""
import json
from pathlib import Path
import shutil
import plistlib
import subprocess
import sys
ROOT = Path(__file__).resolve().parents[1]
app = Path(sys.argv[1])
target = sys.argv[2] if len(sys.argv) > 2 else 'simulator'
shutil.copytree(ROOT / 'build/mono-etc/mono', app / 'Mono/etc/mono', dirs_exist_ok=True)
profile = json.loads((ROOT / 'game/game.json').read_text())
install = ROOT / 'build/steam/download' / Path(profile['installPath']).name
info = plistlib.loads((install / 'Contents/Info.plist').read_bytes())
if info['CFBundleShortVersionString'] != profile['testedGameVersion']:
    raise SystemExit('The downloaded game version has not been validated for game/game.json')
app_info = plistlib.loads((app / 'Info.plist').read_bytes())
# App Store Connect takes only numbers here: the App's version is the Product Release it ships (the
# repository VERSION, the same as the mod inside it) and its build is the game build number. The game
# and the mod identify the game by the full Application.version string, which the host hands to Unity
# from BPPGameVersion before the engine starts (app/Host/main.mm::exposeGameVersion).
app_info['CFBundleShortVersionString'] = (ROOT.parent / 'VERSION').read_text().strip()
app_info['CFBundleVersion'] = profile['testedGameVersion'].split('-')[0].split('.')[-1]
app_info['BPPGameVersion'] = profile['testedGameVersion']
(app / 'Info.plist').write_bytes(plistlib.dumps(app_info))
shutil.copy2(ROOT / 'game/game.json', app / 'game.json')
shutil.copy2(ROOT / 'runtime/helper/entrypoints.json', app / 'entrypoints.json')
shutil.copy2(ROOT / 'build/helper/BazaarPlusPlus.Ios.Helper.dll', app / 'BazaarPlusPlus.Ios.Helper.dll')
shutil.rmtree(app / 'Mods', ignore_errors=True)
shutil.copytree(ROOT / 'build/mods', app / 'Mods')
shutil.copytree(ROOT / 'build/game-adapters', app / 'GameAdapters', dirs_exist_ok=True)
if target == 'simulator':
    plugins = install / profile['pluginPath']
    (app / 'Plugins/support').mkdir(parents=True, exist_ok=True)
    subprocess.run(['xcrun','clang','-target','arm64-apple-ios15.0-simulator','-isysroot', subprocess.check_output(['xcrun','--sdk','iphonesimulator','--show-sdk-path'],text=True).strip(), '-dynamiclib',str(ROOT / 'middleware/mac-plugins/empty.c'),'-o',str(app / 'Plugins/support/libbpp_empty.dylib')],check=True)
    for name, source in profile['simulatorPlugins'].items():
        if name in ('gilzoide-sqlite-net.dylib', 'abci.dylib', 'fmodstudio.dylib', 'resonanceaudio.dylib'):
            (app/'Plugins'/name).unlink(missing_ok=True)
            continue  # Built from pinned source and linked into UnityFramework on both targets.
        subprocess.run(['bash',str(ROOT/'middleware/mac-plugins/retarget-macho.sh'),str(plugins/source),str(app/'Plugins'/name)],check=True)
    subprocess.run(['bash',str(ROOT/'middleware/mac-plugins/retarget-macho.sh'),str(plugins/profile['simulatorBurst']),str(app/'Frameworks/lib_burst_generated.bundle')],check=True)
    subprocess.run(['python3',str(ROOT/'runtime/engine/patch-simulator-runtime.py'),str(app/'Frameworks/UnityFramework.framework/Frameworks/UnityRuntime.framework/UnityRuntime')],check=True)
subprocess.run(['python3', str(ROOT/'service/licenses.py'), str(app)], check=True)
if target == 'simulator':
    for path in [*sorted((app/'Plugins').rglob('*.dylib')), *sorted((app/'Frameworks').glob('*.bundle')), *sorted((app/'Frameworks').rglob('*.framework'),key=lambda p:len(p.parts),reverse=True), *([] if '--xcode' in sys.argv else [app])]:
        subprocess.run(['codesign','-f','-s','-',str(path)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    if '--xcode' not in sys.argv:
        subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)

