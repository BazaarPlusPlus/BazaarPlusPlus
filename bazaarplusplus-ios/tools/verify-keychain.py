#!/usr/bin/env python3
"""Verify the App's Keychain bridge in an isolated Simulator access group, without credentials."""
import argparse
from pathlib import Path
import plistlib
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--device', default='iPhone 18 Pro')
    args = parser.parse_args()
    out = ROOT / 'build/keychain-probe'
    app = out / 'KeychainProbe.app'
    app.mkdir(parents=True, exist_ok=True)
    bundle = 'com.bazaarplusplus.ios.keychainprobe'
    def run(*command, **options):
        return subprocess.run(list(map(str, command)), check=True, **options)
    info = {'CFBundleIdentifier': bundle, 'CFBundleName': 'KeychainProbe',
            'CFBundleExecutable': 'KeychainProbe', 'CFBundlePackageType': 'APPL', 'MinimumOSVersion': '15.0',
            'UIApplicationSceneManifest': {'UIApplicationSupportsMultipleScenes': False,
                'UISceneConfigurations': {'UIWindowSceneSessionRoleApplication': [{
                    'UISceneConfigurationName': 'Probe', 'UISceneDelegateClassName': 'ProbeScene'}]}}}
    (app / 'Info.plist').write_bytes(plistlib.dumps(info))
    # Simulator entitlements are a Mach-O section, not host macOS code-signing privileges.
    entitlements = plistlib.loads((ROOT / 'app/Resources/simulator.entitlements').read_bytes())
    group = 'SIMULATOR.' + bundle
    entitlements.update({'application-identifier': group, 'keychain-access-groups': [group]})
    (out / 'entitlements.plist').write_bytes(plistlib.dumps(entitlements))
    sdk = subprocess.check_output(['xcrun','--sdk','iphonesimulator','--show-sdk-path'],text=True).strip()
    run('xcrun','clang','-target','arm64-apple-ios15.0-simulator','-isysroot',sdk,'-fobjc-arc',
        '-framework','UIKit','-framework','Foundation','-framework','Security',
        ROOT/'service/Native/keychain.m',ROOT/'tests/native/KeychainProbe.m',
        '-Wl,-sectcreate,__TEXT,__entitlements,'+str(out/'entitlements.plist'),'-o',app/'KeychainProbe')
    run('codesign','-f','-s','-',app)
    subprocess.run(['xcrun','simctl','terminate',args.device,bundle],capture_output=True)
    run('xcrun','simctl','install',args.device,app)
    log = out / 'stdout.log'
    log.write_text('')
    try:
        run('xcrun','simctl','launch','--stdout='+str(log),'--stderr='+str(out/'stderr.log'),args.device,bundle)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            if 'STEAM_KEYCHAIN_PROBE PASS status=0' in log.read_text():
                print('PASS: isolated Simulator Keychain write, read and delete.')
                return
            time.sleep(0.2)
        raise RuntimeError('Keychain probe failed; inspect '+str(log))
    finally:
        subprocess.run(['xcrun','simctl','terminate',args.device,bundle],capture_output=True)

if __name__ == '__main__':
    main()
