#!/usr/bin/env python3
"""Build, sign and upload the device App to TestFlight under the given build number.

Signing uses the Apple Distribution certificate and the App Store profile named below, with the team from
app/Signing.local.xcconfig; the upload uses the Apple account signed in to Xcode. Over SSH the keychain that
holds the certificate is locked: set BPP_SIGNING_KEYCHAIN and BPP_SIGNING_KEYCHAIN_PASSWORD_FILE to unlock it.
"""
import argparse
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BUNDLE_ID = 'com.bazaarplusplus.ios'
PROFILE = 'BazaarPlusPlus iOS App Store'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('build', type=int, help='CFBundleVersion; must exceed every build uploaded for this version')
    build = str(parser.parse_args().build)
    team = re.search(r'^DEVELOPMENT_TEAM\s*=\s*(\w+)', (ROOT / 'app/Signing.local.xcconfig').read_text(), re.M).group(1)
    out = ROOT / 'build/testflight' / build
    shutil.rmtree(out, ignore_errors=True)
    out.mkdir(parents=True)
    keychain = os.environ.get('BPP_SIGNING_KEYCHAIN')
    if keychain:
        password = Path(os.environ['BPP_SIGNING_KEYCHAIN_PASSWORD_FILE']).read_text().strip()
        subprocess.run(['security', 'unlock-keychain', '-p', password, keychain], check=True)
    subprocess.run(['python3', str(ROOT / 'tools/build.py'), 'device'], check=True)
    subprocess.run(['python3', str(ROOT / 'tools/audit-device.py')], check=True)
    archive = out / 'BazaarPlusPlus.xcarchive'
    with (out / 'archive.log').open('w') as log:
        subprocess.run(['xcodebuild', '-project', str(ROOT / 'app/BazaarPlusPlus.xcodeproj'), '-scheme', 'BazaarPlusPlus',
                        '-configuration', 'Release', '-sdk', 'iphoneos', '-destination', 'generic/platform=iOS',
                        '-derivedDataPath', str(ROOT / 'build/steam-app/device/launcher-derived'),
                        '-archivePath', str(archive), 'ARCHS=arm64', 'BPP_BUILD_NUMBER=' + build,
                        'CODE_SIGN_STYLE=Manual', 'CODE_SIGN_IDENTITY=Apple Distribution',
                        'PROVISIONING_PROFILE_SPECIFIER=' + PROFILE, 'archive'],
                       stdout=log, stderr=subprocess.STDOUT, check=True)
    # UnityFramework is built before the launcher and embedded prebuilt, so the archive does not collect its symbols.
    shutil.copytree(ROOT / 'build/steam-app/device/derived/Build/Products/Release-iphoneos/UnityFramework.framework.dSYM',
                    archive / 'dSYMs/UnityFramework.framework.dSYM')
    options = out / 'ExportOptions.plist'
    options.write_bytes(plistlib.dumps({
        'method': 'app-store-connect', 'destination': 'upload', 'teamID': team, 'signingStyle': 'manual',
        'signingCertificate': 'Apple Distribution', 'provisioningProfiles': {BUNDLE_ID: PROFILE},
        'manageAppVersionAndBuildNumber': False, 'uploadSymbols': True}))
    with (out / 'upload.log').open('w') as log:
        subprocess.run(['xcodebuild', '-exportArchive', '-archivePath', str(archive), '-exportPath', str(out / 'export'),
                        '-exportOptionsPlist', str(options), '-allowProvisioningUpdates'],
                       stdout=log, stderr=subprocess.STDOUT, check=True)
    info = plistlib.loads((archive / 'Products/Applications/BazaarPlusPlus.app/Info.plist').read_bytes())
    print(f"Uploaded {info['CFBundleShortVersionString']} ({info['CFBundleVersion']})")

if __name__ == '__main__':
    main()
