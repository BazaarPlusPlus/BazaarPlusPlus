#!/usr/bin/env python3
"""Prepare a pinned device engine archive before linking/signing; never modify executable pages at runtime."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
ROOT = Path(__file__).resolve().parents[2]
DECOMPRESS = '__Z23DecompressNativeTexture14GraphicsFormatS_iiPKjiiPj'
DECOMPRESS_TEXTURE = '__Z23DecompressNativeTexture13TextureFormatS_iiPKjiiPj'
READ_METADATA = '__ZN14SerializedFile12ReadMetadataE27SerializedFileFormatVersionN3VFS8FileSizeEPKhmS2_'

class ObjectFile:
    def __init__(self, data):
        self.data = bytearray(data)
        if struct.unpack_from('<I', data)[0] != 0xfeedfacf or struct.unpack_from('<I', data, 12)[0] != 1:
            raise ValueError('Expected a thin Mach-O object')
        self.sections, self.symbols = [], {}
        pos = 32
        for _ in range(struct.unpack_from('<I', data, 16)[0]):
            cmd, size = struct.unpack_from('<II', data, pos)
            if cmd == 0x19:
                for i in range(struct.unpack_from('<I', data, pos + 64)[0]):
                    off = pos + 72 + 80 * i
                    addr, length, fileoff = struct.unpack_from('<QQI', data, off + 32)
                    self.sections.append((addr, length, fileoff))
            elif cmd == 2:
                symoff, count, stroff, strlen = struct.unpack_from('<IIII', data, pos + 8)
                for i in range(count):
                    entry = symoff + 16 * i
                    index, kind, section, desc, value = struct.unpack_from('<IBBHQ', data, entry)
                    name = data[stroff+index:data.index(b'\0',stroff+index)].decode()
                    self.symbols[name] = (value, kind, stroff+index)
            pos += size
    def offset(self, address):
        for addr, size, off in self.sections:
            if addr <= address < addr + size: return off + address - addr
        raise ValueError('Address outside object sections')
    def rename_definition(self, old, new):
        _, kind, pos = self.symbols[old]
        if kind & 0x0e != 0x0e or len(new) > len(old): raise ValueError('Not a renameable definition')
        self.data[pos:pos+len(old)+1] = new.encode() + b'\0' * (len(old)+1-len(new))
    def patch_target(self):
        start = self.offset(self.symbols[READ_METADATA][0])
        # ldur w9,[x10,#1]; add x8,x10,#5; str x8,[sp,#0x100]; str w9,[x20,#0x94]
        expected = [0xb8401149, 0x91001548, 0xf90083e8, 0xb9009689]
        pattern = struct.pack('<4I', *expected)
        hits = [i for i in range(start, start+0x120, 4) if self.data[i:i+16] == pattern]
        if len(hits) != 1: raise ValueError('SerializedFile target instruction sequence changed')
        struct.pack_into('<I', self.data, hits[0], 0x52800129)  # mov w9,#9 (iOS)
        return hits[0]

def prepare(source, output):
    lock = json.loads((ROOT/'toolchain.lock.json').read_text())['deviceEngine']
    if hashlib.sha256(source.read_bytes()).hexdigest() != lock['sha256']:
        raise ValueError('Unvalidated Unity device engine archive')
    output.mkdir(parents=True, exist_ok=True)
    archive = output/'libiPhone-lib.a'; shutil.copy2(source, archive)
    receipt = {'sourceSha256': lock['sha256'], 'objects': {}}
    for member, mode in [(lock['graphicsObject'],'decoder'),(lock['serializeObject'],'target')]:
        data = subprocess.check_output(['xcrun','ar','-p',str(source),member])
        obj = ObjectFile(data)
        if mode == 'decoder':
            obj.rename_definition(DECOMPRESS, '_bpp_engine_decompress_original')
            obj.rename_definition(DECOMPRESS_TEXTURE, '_bpp_engine_decompress_texture_original')
        else: receipt['targetPatchOffset'] = obj.patch_target()
        (output/member).write_bytes(obj.data)
        subprocess.run(['xcrun','ar','-r',str(archive),str(output/member)],check=True)
        receipt['objects'][member] = hashlib.sha256(obj.data).hexdigest()
    subprocess.run(['xcrun','ranlib',str(archive)],check=True)
    symbols = subprocess.check_output(['xcrun','nm','-gU',str(source)],text=True,stderr=subprocess.DEVNULL)
    names = sorted(set(re.findall(r'__Z\d+(Register_\w+)v\b', symbols)))
    if len(names) != lock['icallRegistrations']: raise ValueError('Unexpected engine registration inventory')
    generated = '// Generated from the pinned device engine; keeps APIs used by installed assemblies.\n'
    generated += ''.join('void '+name+'();\n' for name in names)
    generated += 'void RegisterAllStrippedInternalCalls() {\n' + ''.join('  '+name+'();\n' for name in names) + '}\n'
    (output/'UnityICallRegistration.cpp').write_text(generated)
    receipt['icallRegistrations'] = len(names)
    (output/'engine-preparation.json').write_text(json.dumps(receipt,indent=2)+'\n')
    return archive

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__); parser.add_argument('source',type=Path); parser.add_argument('output',type=Path)
    args=parser.parse_args(); print(prepare(args.source,args.output))
