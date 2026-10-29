#!/usr/bin/env python3
"""Build BazaarPlusPlus (the Mono host assembly) and stage what the App ships in Mods/.

The managed files are the desktop host's (`release/payload.json`, host `bepinex`, plugins
folder, no platform filter): the iOS port loads the same assemblies under its own entry point.
The Harmony runtime is the BepInEx core set the installer ships, without the BepInEx loader.
"""
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
MOD = REPO / 'bazaarplusplus-mod/src/BazaarPlusPlus'
CORE = REPO / 'bazaarplusplus-installer/src-tauri/resources/SourceForBuild/macos/BepInEx/core'
OUT = ROOT / 'build/mods'

subprocess.run(['dotnet', 'build', str(MOD / 'BazaarPlusPlus.csproj'), '-c', 'Release', '-nologo', '-v', 'q',
                '-p:BppDeployToGame=false'], check=True)
built = MOD / 'bin/Release/netstandard2.1'
shutil.rmtree(OUT, ignore_errors=True)
OUT.mkdir(parents=True)
for entry in json.loads((REPO / 'release/payload.json').read_text())['files']:
    path = entry['path']
    if (entry['host'] != 'bepinex' or entry['producer'] not in ('managed', 'history-contract')
            or entry.get('platforms') or not path.startswith('BepInEx/plugins/')):
        continue
    source = built / Path(path).name
    if source.suffix == '.version':
        continue  # Written only by the production package; without it the assemblies report VERSION.
    if source.exists():
        shutil.copy2(source, OUT / source.name)
    elif not entry.get('optional'):
        raise SystemExit(f'{source} is missing from the mod build')
# BepInEx.dll comes along only so the desktop adapter types (Plugin : BaseUnityPlugin) load when
# Unity enumerates the mod's types; nothing on iOS starts BepInEx. Its preloader and patchers stay out.
for library in sorted(CORE.glob('*.dll')):
    if library.name == 'BepInEx.dll' or not library.name.startswith('BepInEx'):
        shutil.copy2(library, OUT / library.name)
print('Staged ' + ', '.join(sorted(p.name for p in OUT.iterdir())))
