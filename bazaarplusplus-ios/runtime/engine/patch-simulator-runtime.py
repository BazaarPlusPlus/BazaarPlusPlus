#!/usr/bin/env python3
"""Patch the prebuilt iOS UnityRuntime so it can run another platform's player data.

Every patch checks the exact instructions it replaces, so a different engine build fails loudly
instead of being corrupted. Patches:

  build-target     SerializedFile::ReadMetadata rejects files whose target platform is not iOS (9).
                   The platform load becomes `mov w10, #9`, so Mac data and AssetBundles load as-is.
  dev-console      DeveloperConsole's constructor starts enabled; it now starts closed and disabled,
                   so engine errors do not paint the IMGUI console over the game.
  hook:<name>      Replaces a code path with a hook stub. The stub carries a marker
                   (movz/movk x17 = 'UR','HK',<id>), loads a function pointer from a free 8-byte slot
                   at the end of __DATA, and calls it; an empty slot keeps the engine's original
                   failure result. The shim (runtime/bridge/engine_hooks.c) finds the markers at load time
                   and fills the slots.

Hooks (ids match runtime/bridge/engine_hooks.h):
  1 decompress-native-texture   DecompressNativeTexture's unsupported-format path (it only
                                decodes ASTC/ETC on iOS) calls bpp_decompress_native_texture,
                                which decodes BC1-BC7.

usage: patch-unityruntime.py <UnityRuntime>
"""
import struct
import subprocess
import sys

LC_SEGMENT_64 = 0x19


def word(data, off):
    return struct.unpack_from('<I', data, off)[0]


def put(data, off, w):
    struct.pack_into('<I', data, off, w)


def symbols(path):
    out = subprocess.run(['nm', path], capture_output=True, text=True, check=True).stdout
    table = {}
    for line in out.splitlines():
        parts = line.split()
        if len(parts) == 3:
            table[parts[2]] = int(parts[0], 16)
    return table


def data_slot_area(data):
    """File offset and vm address of free space between __DATA's last section and the segment end."""
    ncmds = struct.unpack_from('<I', data, 16)[0]
    off = 32
    for _ in range(ncmds):
        cmd, size = struct.unpack_from('<II', data, off)
        if cmd == LC_SEGMENT_64:
            segname = data[off + 8:off + 24].rstrip(b'\0').decode()
            vmaddr, vmsize = struct.unpack_from('<QQ', data, off + 24)
            nsects = struct.unpack_from('<I', data, off + 64)[0]
            if segname == '__DATA':
                end = vmaddr
                for i in range(nsects):
                    s = off + 72 + i * 80
                    addr, sz = struct.unpack_from('<QQ', data, s + 32)
                    end = max(end, addr + sz)
                return (end + 63) & ~63, vmaddr + vmsize
        off += size
    sys.exit('no __DATA segment')


# --- instruction encoders (arm64) ---
def movz(rd, imm16, shift=0, x=True):
    return (0xD2800000 if x else 0x52800000) | (shift // 16) << 21 | imm16 << 5 | rd


def movk(rd, imm16, shift):
    return 0xF2800000 | (shift // 16) << 21 | imm16 << 5 | rd


def mov_reg(rd, rm):  # orr xd, xzr, xm
    return 0xAA0003E0 | rm << 16 | rd


def adrp(rd, pc, target):
    delta = (target >> 12) - (pc >> 12)
    immlo, immhi = delta & 3, (delta >> 2) & 0x7FFFF
    return 0x90000000 | immlo << 29 | immhi << 5 | rd


def ldr_x_uimm(rt, rn, imm):  # ldr xt, [xn, #imm]
    assert imm % 8 == 0
    return 0xF9400000 | (imm // 8) << 10 | rn << 5 | rt


def cbz_x(rt, pc, target):
    return 0xB4000000 | (((target - pc) >> 2) & 0x7FFFF) << 5 | rt


def cbz_w(rt, pc, target):
    return 0x34000000 | (((target - pc) >> 2) & 0x7FFFF) << 5 | rt


def b(pc, target):
    return 0x14000000 | (((target - pc) >> 2) & 0x3FFFFFF)


def blr(rn):
    return 0xD63F0000 | rn << 5


MARK_UR, MARK_HK = 0x5552, 0x484B


def hook_stub(pc, hook_id, slot, arg_setup, fail, success):
    code = [movz(17, MARK_UR), movk(17, MARK_HK, 16), movk(17, hook_id, 32)]
    code.append(adrp(16, pc + 4 * len(code), slot))
    code.append(ldr_x_uimm(16, 16, slot & 0xFFF))
    code.append(None)  # cbz x16, fail (needs its pc)
    code += arg_setup
    code.append(blr(16))
    code.append(None)  # cbz w0, fail
    code.append(None)  # b success
    words = []
    for i, w in enumerate(code):
        at = pc + 4 * i
        if w is None:
            if i == 5:
                w = cbz_x(16, at, fail)
            elif i == len(code) - 2:
                w = cbz_w(0, at, fail)
            else:
                w = b(at, success)
        words.append(w)
    return words


def find(data, start, end, words):
    for off in range(start, end, 4):
        if all(word(data, off + 4 * i) == w for i, w in enumerate(words)):
            return off
    return None


def patch_build_target(data):
    LDUR_W10_X9_1, MOV_W10_9, CMP_W10_9 = 0xB840112A, 0x5280012A, 0x7100255F
    hits = [o for o in range(0, len(data) - 32, 4)
            if word(data, o) in (LDUR_W10_X9_1, MOV_W10_9) and CMP_W10_9 in struct.unpack_from('<6I', data, o + 4)]
    if len(hits) != 1:
        sys.exit(f'build-target: expected one site, found {len(hits)}')
    put(data, hits[0], MOV_W10_9)
    return f'build-target at {hits[0]:#x}'


def patch_dev_console(data, syms):
    ctor = syms['__ZN16DeveloperConsoleC2Ev']
    MOV_W8_0x100, MOV_W8_1 = 0x52802008, 0x52800028  # bytes {closed, enabled}: {0,1} -> {1,0}
    site = find(data, ctor, ctor + 64, [MOV_W8_0x100]) or find(data, ctor, ctor + 64, [MOV_W8_1])
    if site is None:
        sys.exit('dev-console: constructor pattern not found')
    put(data, site, MOV_W8_1)
    return f'dev-console at {site:#x}'


def patch_decompress_hook(data, syms, slot):
    fn = syms['__Z23DecompressNativeTexture14GraphicsFormatS_iiPKjiiPj']
    # Arguments as the prologue spills them; the hook needs all of them at the failure path.
    prologue = [0xF9001BE3,  # str x3, [sp, #0x30]   height
                ]
    if find(data, fn, fn + 0x40, prologue) is None:
        sys.exit('decompress hook: prologue changed')
    saved = [0xAA0703F8, 0xAA0603F3, 0xAA0503F6, 0xAA0203FB, 0xAA0103F7]  # mov x24,x7 x19,x6 x22,x5 x27,x2 x23,x1
    if find(data, fn, fn + 0x60, saved) is None:
        sys.exit('decompress hook: argument registers changed')
    # sub w8, w21, #0x6f ; cmp w8, #0x27 ; b.hi <unsupported>
    sub_cmp = find(data, fn, fn + 0x80, [0x5101BEA8, 0x71009D1F])
    if sub_cmp is None:
        sys.exit('decompress hook: format range check not found')
    bhi = word(data, sub_cmp + 8)
    if bhi & 0xFF00001F != 0x54000008:
        sys.exit('decompress hook: expected b.hi')
    imm19 = (bhi >> 5) & 0x7FFFF
    unsupported = sub_cmp + 8 + ((imm19 - (1 << 19) if imm19 & (1 << 18) else imm19) << 2)
    # The failure tail: mov w0, #0 ; ldur x8, [x29, #-0x60] (stack-guard check, then return).
    fail = find(data, unsupported, unsupported + 0x200, [0x52800000, 0xF85A03A8])
    if fail is None:
        sys.exit('decompress hook: failure tail not found')
    success = fail + 4
    if word(data, unsupported) == movz(17, MARK_UR):
        return f'hook decompress-native-texture already at {unsupported:#x}'
    arg_setup = [
        mov_reg(1, 0),            # dst format (linear), still in x0 after GetLinearFormat
        mov_reg(0, 21),           # src format (linear)
        mov_reg(2, 27),           # width
        0xF9401BE3,               # ldr x3, [sp, #0x30]  height
        0xF94017E4,               # ldr x4, [sp, #0x28]  src
        mov_reg(5, 22),           # dst width
        mov_reg(6, 19),           # dst height
        mov_reg(7, 24),           # dst
    ]
    stub = hook_stub(unsupported, 1, slot, arg_setup, fail, success)
    if unsupported + 4 * len(stub) > fail:
        sys.exit('decompress hook: stub does not fit')
    for i, w in enumerate(stub):
        put(data, unsupported + 4 * i, w)
    return f'hook decompress-native-texture at {unsupported:#x}, slot {slot:#x}'


def main(path):
    data = bytearray(open(path, 'rb').read())
    syms = symbols(path)
    slot_start, slot_end = data_slot_area(data)
    if slot_start + 8 > slot_end:
        sys.exit('no free __DATA space for hook slots')
    results = [
        patch_build_target(data),
        patch_dev_console(data, syms),
        patch_decompress_hook(data, syms, slot_start),
    ]
    open(path, 'wb').write(data)
    print('; '.join(results))


if __name__ == '__main__':
    main(sys.argv[1])
