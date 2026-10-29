#!/usr/bin/env python3
"""Write an evidence-based device readiness report without executing game assemblies."""
import argparse, collections, json, re, subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def exports(path):
    return set(subprocess.check_output(['xcrun','nm','-gjU',str(path)],text=True,stderr=subprocess.DEVNULL).splitlines())
def audit(app, game, output):
    subprocess.run(['dotnet','build',str(ROOT/'tools/game-audit'),'-c','Release','--nologo'],check=True,stdout=subprocess.DEVNULL)
    metadata=json.loads(subprocess.check_output(['dotnet',str(ROOT/'tools/game-audit/bin/Release/net10.0/GameAudit.dll'),str(game/'Contents/Resources/Data/Managed')],text=True))
    symbols=exports(app/'Frameworks/UnityFramework.framework/UnityFramework')
    groups=collections.defaultdict(set)
    for item in metadata['imports']: groups[item['library']].add(item['entry'])
    components={}
    for name in ['steam_api','gilzoide-sqlite-net','sqlite3','abci','fmodstudio']:
        missing=sorted(s for s in groups[name] if '_'+s not in symbols)
        components[name]={'requiredExports':len(groups[name]),'resolvedExports':len(groups[name])-len(missing),'missingExports':missing}
    # Compare declaration gaps with the original shipped Mac plugin; don't invent replacement APIs.
    original=exports(game/'Contents/PlugIns/abci.bundle/Contents/MacOS/abci')
    components['abci']['alsoMissingFromOriginalPlugin']=sorted(s for s in groups['abci'] if '_'+s not in original)
    # Registration functions are directly called and may be local symbols after linking.
    retained=set(subprocess.check_output(['xcrun','nm','-jU',str(app/'Frameworks/UnityFramework.framework/UnityFramework')],text=True).splitlines())
    register={m.group(1) for s in retained if (m:=re.fullmatch(r'__Z\d+(Register_\w+)v',s))}
    source=ROOT/'build/steam-app/device/export/Il2CppOutputProject/Source/il2cppOutput/UnityICallRegistration.cpp'
    expected=set(re.findall(r'void (Register_\w+)\(',source.read_text()))
    if expected-register:raise ValueError('Registered icalls were stripped from the linked framework')
    required=['_bpp_native_selftest','_bpp_steam_start','_mono_aot_module_mscorlib_info',
              '_bpp_fmod_selftest','_bpp_fmod_register_resonance','_bpp_fmod_studio_initialize','_bpp_fmod_studio_release',
              '_bpp_fmod_mixer_suspend','_bpp_fmod_mixer_resume',
              '_FMOD_ResonanceAudioListener_GetDSPDescription','_FMOD_ResonanceAudioSoundfield_GetDSPDescription',
              '_FMOD_ResonanceAudioSource_GetDSPDescription']
    if any(s not in symbols for s in required):raise ValueError('Missing required device entrypoint')
    if '_bpp_engine_decompress_original' not in retained:raise ValueError('Missing original texture fallback')
    if components['gilzoide-sqlite-net']['missingExports']:raise ValueError('SQLite ABI incomplete')
    if components['fmodstudio']['missingExports']:raise ValueError('FMOD ABI incomplete')
    if set(components['abci']['missingExports'])-set(components['abci']['alsoMissingFromOriginalPlugin']):raise ValueError('Alembic ABI regressed from original plugin')
    report={'platform':'iphoneos-arm64','physicalDeviceTested':False,'engineIcalls':{'generated':len(expected),'retained':len(register)},
      'resourceAdapter':'linked BC1-BC7 wrapper and pinned SerializedFile target patch','nativeLibraries':components,
      'fmodRequired':json.loads((ROOT/'toolchain.lock.json').read_text())['fmodRequired'],
      'audio':{'sdk':json.loads((ROOT/'build/fmod-device/receipt.json').read_text()),
               'managedAdapter':json.loads((app/'GameAdapters/manifest.json').read_text())},
      'remaining':[
        'Signed device install and native startup self-check','Physical device Metal, resources, audio, input and memory validation'],
      'launchAllowed':False}
    output.parent.mkdir(parents=True,exist_ok=True);output.write_text(json.dumps(report,indent=2)+'\n');print(output)
if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--app',type=Path,default=ROOT/'build/steam-app/device/launcher-derived/Build/Products/Release-iphoneos/BazaarPlusPlus.app');p.add_argument('--game',type=Path,default=ROOT/'build/steam/download/TheBazaar.app');p.add_argument('--output',type=Path,default=ROOT/'build/device-audit/readiness.json');a=p.parse_args();audit(a.app,a.game,a.output)
