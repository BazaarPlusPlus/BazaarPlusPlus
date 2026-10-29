#!/usr/bin/env python3
"""Install the Simulator test build and open its local integration-test channel."""
import json
import os
import plistlib
from pathlib import Path
import subprocess
import uuid
import time
ROOT = Path(__file__).resolve().parents[1]
os.umask(0o077)
device = os.environ.get('BPP_DEVICE', 'iPhone 18 Pro Max')
app = ROOT / 'build/steam-app/simulator/launcher-derived/Build/Products/Release-iphonesimulator/BazaarPlusPlus.app'
bundle = plistlib.loads((app / 'Info.plist').read_bytes())['CFBundleIdentifier']
output = ROOT / 'build/steam-app/verification'
output.mkdir(parents=True, exist_ok=True)
socket = '/tmp/bpp-steam-' + uuid.uuid4().hex[:12] + '.sock'
subprocess.run(['xcrun','simctl','terminate',device,bundle],capture_output=True)
subprocess.run(['xcrun','simctl','install',device,str(app)],check=True)
env = {k:v for k,v in os.environ.items() if not k.startswith('SIMCTL_CHILD_')}
env['SIMCTL_CHILD_BPP_STEAM_TEST_SOCKET'] = socket
# BPP_* switches (BPP_SELFTEST, BPP_HARMONY_SPIKE, ...) reach the app's environment.
env.update({'SIMCTL_CHILD_'+k: v for k, v in os.environ.items() if k.startswith('BPP_') and k != 'BPP_DEVICE'})
subprocess.run(['xcrun','simctl','launch','--stdout='+str(output/'stdout.log'),'--stderr='+str(output/'stderr.log'),device,bundle],env=env,check=True)
(output/'control.json').write_text(json.dumps({'socket':socket,'device':device})+'\n')
deadline = time.monotonic() + 30
while not Path(socket).exists():
    if time.monotonic() >= deadline:
        raise SystemExit('App did not initialize its test channel; inspect verification/stderr.log')
    time.sleep(0.1)
print('Launched embedded Steam app. Test channel metadata: '+str(output/'control.json'))
