#!/usr/bin/env python3
"""Verify a running game's FMOD banks/mixer and synthetic foreground lifecycle."""
import json
from pathlib import Path
import socket
import time

ROOT = Path(__file__).resolve().parents[2]

def request(action):
    metadata = json.loads((ROOT / 'build/steam-app/verification/control.json').read_text())
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
        client.settimeout(10)
        client.connect(metadata['socket'])
        client.sendall(json.dumps({'action': action}).encode() + b'\n')
        chunks = []
        while data := client.recv(4096):
            chunks.append(data)
    return json.loads(b''.join(chunks))

def main():
    observations = {'playing': request('audio-snapshot')}
    if observations['playing'].get('systems') != 1 or observations['playing'].get('realChannels', 0) <= 0:
        raise RuntimeError('Start the game and wait for audible menu content before testing')
    try:
        observations['background'] = request('audio-background')
        time.sleep(0.4)
        observations['paused'] = request('audio-snapshot')
    finally:
        observations['foreground'] = request('audio-foreground')
    time.sleep(0.5)
    observations['resumed'] = request('audio-snapshot')
    if not all(s.get('result') == 0 and s.get('systems') == 1 and s.get('banks', 0) > 0
               for s in observations.values()):
        raise RuntimeError('FMOD system or bank query failed')
    if not observations['paused']['suspended'] or observations['background']['dspClock'] != observations['paused']['dspClock']:
        raise RuntimeError('FMOD mixer did not stop on background notification')
    if observations['resumed']['suspended'] or observations['resumed']['dspClock'] <= observations['foreground']['dspClock']:
        raise RuntimeError('FMOD mixer did not resume on foreground notification')
    output = ROOT / 'build/device-audit/fmod-simulator-synthetic-lifecycle.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({'kind': 'synthetic UIApplication notifications, not physical-device evidence',
                                  'snapshots': observations}, indent=2) + '\n')
    print('PASS: banks loaded, real channels playing, DSP clock stops and resumes. ' + str(output))

if __name__ == '__main__':
    main()
