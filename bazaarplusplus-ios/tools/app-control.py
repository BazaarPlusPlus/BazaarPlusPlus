#!/usr/bin/env python3
"""Exercise the opt-in Simulator service channel; passwords are prompted, never CLI arguments."""
import argparse
import getpass
import json
from pathlib import Path
import socket
ROOT = Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('action',choices=['snapshot','login','restore','qr','guard','download','launch','cancel','logout',
                                   'audio-snapshot','audio-background','audio-foreground'])
args=parser.parse_args()
request={'action':args.action}
if args.action=='login':
    request['username']=input('Steam account: ').strip()
    request['password']=getpass.getpass('Steam password: ')
if args.action=='guard': request['code']=getpass.getpass('Steam Guard code: ')
metadata=json.loads((ROOT/'build/steam-app/verification/control.json').read_text())
with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as client:
    client.settimeout(30)
    client.connect(metadata['socket'])
    client.sendall(json.dumps(request).encode()+b'\n')
    request.clear()
    chunks=[]
    while True:
        data=client.recv(4096)
        if not data: break
        chunks.append(data)
print(b''.join(chunks).decode())
