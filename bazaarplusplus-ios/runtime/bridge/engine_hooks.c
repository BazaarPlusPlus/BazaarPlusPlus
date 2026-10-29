// Engine hooks: functions the patched UnityRuntime calls through hook stubs (runtime/engine/patch-simulator-runtime.py).
//
// A stub starts with the marker movz/movk x17 = 'UR' | 'HK' << 16 | id << 32, followed by
// `adrp x16, slot` + `ldr x16, [x16, #slot]`. When UnityRuntime loads, we find each marker in its
// __text, decode the slot address, and store the hook's implementation there. A slot left empty
// makes the stub return the engine's original failure result.
#include <dispatch/dispatch.h>
#include <mach-o/dyld.h>
#include <mach-o/getsect.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>

#define BCDEC_IMPLEMENTATION
#define BCDEC_BC4BC5_PRECISE
#include "../../third_party/bcdec/bcdec.h"

void bpp_log(const char *fmt, ...);

// GraphicsFormat values (UnityEngine.Experimental.Rendering.GraphicsFormat). The engine passes the
// linear variant of both formats.
enum {
    GF_R8_UNorm = 5, GF_R8G8_UNorm = 6, GF_R8G8B8A8_UNorm = 8,
    GF_R16G16B16A16_SFloat = 48, GF_R32G32B32A32_SFloat = 52,
    GF_DXT1_UNorm = 97, GF_DXT3_UNorm = 99, GF_DXT5_UNorm = 101,
    GF_BC4_UNorm = 102, GF_BC4_SNorm = 103, GF_BC5_UNorm = 104, GF_BC5_SNorm = 105,
    GF_BC6H_UFloat = 106, GF_BC6H_SFloat = 107, GF_BC7_UNorm = 109,
};

static uint16_t float_to_half(float f) {
    union { float f; uint32_t u; } v = {f};
    uint32_t sign = (v.u >> 16) & 0x8000, exp = (v.u >> 23) & 0xFF, man = v.u & 0x7FFFFF;
    if (exp == 0xFF) return (uint16_t)(sign | 0x7C00 | (man ? 0x200 : 0));
    int e = (int)exp - 127 + 15;
    if (e >= 31) return (uint16_t)(sign | 0x7C00);
    if (e <= 0) {
        if (e < -10) return (uint16_t)sign;
        return (uint16_t)(sign | ((man | 0x800000) >> (14 - e)));
    }
    return (uint16_t)(sign | (uint32_t)e << 10 | man >> 13);
}

// Writes one decoded texel (as RGBA float) into the destination format.
static uint8_t unorm8(float value) {
    value = value < 0 ? 0 : value > 1 ? 1 : value;
    return (uint8_t)(value * 255.0f + 0.5f);
}
static bool store_texel(int dst_format, uint8_t *dst, const float rgba[4]) {
    switch (dst_format) {
    case GF_R8G8B8A8_UNorm:
        for (int c = 0; c < 4; c++) {
            float v = rgba[c] < 0 ? 0 : rgba[c] > 1 ? 1 : rgba[c];
            dst[c] = (uint8_t)(v * 255.0f + 0.5f);
        }
        return true;
    case GF_R8_UNorm: dst[0] = unorm8(rgba[0]); return true;
    case GF_R8G8_UNorm: dst[0] = unorm8(rgba[0]); dst[1] = unorm8(rgba[1]); return true;
    case GF_R16G16B16A16_SFloat:
        for (int c = 0; c < 4; c++) ((uint16_t *)dst)[c] = float_to_half(rgba[c]);
        return true;
    case GF_R32G32B32A32_SFloat: memcpy(dst, rgba, 16); return true;
    default: return false;
    }
}

static int texel_size(int format) {
    switch (format) {
    case GF_R8_UNorm: return 1;
    case GF_R8G8_UNorm: return 2;
    case GF_R8G8B8A8_UNorm: return 4;
    case GF_R16G16B16A16_SFloat: return 8;
    case GF_R32G32B32A32_SFloat: return 16;
    default: return 0;
    }
}

struct decode_job {
    int src_format, dst_format, width, height, dst_width, dst_height, block_bytes, out_size, blocks_x, blocks_y;
    int direct; // the decoder's own 8-bit output is dst_format: write texels without converting
    const uint8_t *src;
    uint8_t *dst;
    size_t dst_pitch;
};

// Decodes one block into 8-bit texels at out (row pitch in bytes), for the formats with a direct path.
static void decode_block_direct(const struct decode_job *j, const uint8_t *block, uint8_t *out, int pitch) {
    switch (j->src_format) {
    case GF_DXT1_UNorm: bcdec_bc1(block, out, pitch); break;
    case GF_DXT3_UNorm: bcdec_bc2(block, out, pitch); break;
    case GF_DXT5_UNorm: bcdec_bc3(block, out, pitch); break;
    case GF_BC7_UNorm: bcdec_bc7(block, out, pitch); break;
    case GF_BC4_UNorm: bcdec_bc4(block, out, pitch, 0); break;
    case GF_BC5_UNorm: bcdec_bc5(block, out, pitch, 0); break;
    }
}

// Decodes one block to RGBA float texels.
static void decode_block_float(int src_format, const uint8_t *block, float texels[16][4]) {
    uint8_t rgba8[16 * 4];
    float channels[16 * 2];
    switch (src_format) {
    case GF_DXT1_UNorm: case GF_DXT3_UNorm: case GF_DXT5_UNorm: case GF_BC7_UNorm:
        if (src_format == GF_DXT1_UNorm) bcdec_bc1(block, rgba8, 16);
        else if (src_format == GF_DXT3_UNorm) bcdec_bc2(block, rgba8, 16);
        else if (src_format == GF_DXT5_UNorm) bcdec_bc3(block, rgba8, 16);
        else bcdec_bc7(block, rgba8, 16);
        for (int i = 0; i < 16; i++)
            for (int c = 0; c < 4; c++) texels[i][c] = rgba8[i * 4 + c] / 255.0f;
        break;
    case GF_BC4_UNorm: case GF_BC4_SNorm:
        bcdec_bc4_float(block, channels, 4, src_format == GF_BC4_SNorm);
        for (int i = 0; i < 16; i++) {
            texels[i][0] = channels[i];
            texels[i][1] = texels[i][2] = 0; texels[i][3] = 1;
        }
        break;
    case GF_BC5_UNorm: case GF_BC5_SNorm:
        bcdec_bc5_float(block, channels, 8, src_format == GF_BC5_SNorm);
        for (int i = 0; i < 16; i++) {
            texels[i][0] = channels[i * 2]; texels[i][1] = channels[i * 2 + 1];
            texels[i][2] = 0; texels[i][3] = 1;
        }
        break;
    default: { // BC6H
        float rgb[16 * 3];
        bcdec_bc6h_float(block, rgb, 4 * 3, src_format == GF_BC6H_SFloat);
        for (int i = 0; i < 16; i++) {
            texels[i][0] = rgb[i * 3]; texels[i][1] = rgb[i * 3 + 1]; texels[i][2] = rgb[i * 3 + 2];
            texels[i][3] = 1;
        }
        break;
    }
    }
}

static void decode_block_row(const struct decode_job *j, int by) {
    int rows = j->height - by * 4, cols_limit = j->width < j->dst_width ? j->width : j->dst_width;
    if (rows > 4) rows = 4;
    if (by * 4 + rows > j->dst_height) rows = j->dst_height - by * 4;
    if (rows <= 0) return;
    for (int bx = 0; bx < j->blocks_x; bx++) {
        const uint8_t *block = j->src + ((size_t)by * j->blocks_x + bx) * j->block_bytes;
        uint8_t *out = j->dst + (size_t)by * 4 * j->dst_pitch + (size_t)bx * 4 * j->out_size;
        int cols = cols_limit - bx * 4;
        if (cols > 4) cols = 4;
        if (cols <= 0) break;
        if (j->direct) {
            if (rows == 4 && cols == 4) {
                decode_block_direct(j, block, out, (int)j->dst_pitch);
            } else {
                uint8_t tmp[16 * 4];
                decode_block_direct(j, block, tmp, 4 * j->out_size);
                for (int y = 0; y < rows; y++) memcpy(out + y * j->dst_pitch, tmp + y * 4 * j->out_size, (size_t)cols * j->out_size);
            }
            continue;
        }
        float texels[16][4];
        decode_block_float(j->src_format, block, texels);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                store_texel(j->dst_format, out + y * j->dst_pitch + (size_t)x * j->out_size, texels[y * 4 + x]);
    }
}

// Hook 1: called from DecompressNativeTexture for formats the iOS engine cannot decode itself.
// Decodes a width x height BC image into a dst_width x dst_height image of dst_format. It runs on the
// engine's render thread, which the main thread waits on every frame, so large images are decoded in
// parallel across all cores.
bool bpp_decompress_native_texture(int src_format, int dst_format, int width, int height, const uint8_t *src,
                                  int dst_width, int dst_height, uint8_t *dst) {
    if (width <= 0 || height <= 0 || dst_width <= 0 || dst_height <= 0 ||
        width > INT_MAX - 3 || height > INT_MAX - 3 || dst_width > INT_MAX / 16) return false;
    static int logged;
    if (logged < 8) {
        logged++;
        bpp_log("decompress src=%d dst=%d %dx%d -> %dx%d", src_format, dst_format, width, height, dst_width, dst_height);
    }
    struct decode_job j = {src_format, dst_format, width, height, dst_width, dst_height};
    switch (src_format) {
    case GF_DXT1_UNorm: case GF_BC4_UNorm: case GF_BC4_SNorm: j.block_bytes = 8; break;
    case GF_DXT3_UNorm: case GF_DXT5_UNorm: case GF_BC5_UNorm: case GF_BC5_SNorm:
    case GF_BC6H_SFloat: case GF_BC6H_UFloat: case GF_BC7_UNorm: j.block_bytes = 16; break;
    default: return false;
    }
    j.out_size = texel_size(dst_format);
    if (!j.out_size || !src || !dst) return false;
    switch (src_format) {
    case GF_DXT1_UNorm: case GF_DXT3_UNorm: case GF_DXT5_UNorm: case GF_BC7_UNorm: j.direct = dst_format == GF_R8G8B8A8_UNorm; break;
    case GF_BC4_UNorm: j.direct = dst_format == GF_R8_UNorm; break;
    case GF_BC5_UNorm: j.direct = dst_format == GF_R8G8_UNorm; break;
    }
    j.src = src; j.dst = dst;
    j.dst_pitch = (size_t)dst_width * j.out_size;
    j.blocks_x = (width + 3) / 4; j.blocks_y = (height + 3) / 4;
    if ((size_t)j.blocks_x * j.blocks_y < 4096) {
        for (int by = 0; by < j.blocks_y; by++) decode_block_row(&j, by);
        return true;
    }
    const int rows_per_chunk = 8;
    size_t chunks = (size_t)(j.blocks_y + rows_per_chunk - 1) / rows_per_chunk;
    dispatch_apply(chunks, dispatch_get_global_queue(QOS_CLASS_USER_INTERACTIVE, 0), ^(size_t c) {
        int end = (int)(c + 1) * rows_per_chunk;
        if (end > j.blocks_y) end = j.blocks_y;
        for (int by = (int)c * rows_per_chunk; by < end; by++) decode_block_row(&j, by);
    });
    return true;
}

#ifndef BPP_TEXTURE_DECODER_ONLY
static void *const g_hooks[] = {
    [1] = (void *)bpp_decompress_native_texture,
};

static int64_t sign_extend(uint64_t v, int bits) { return (int64_t)(v << (64 - bits)) >> (64 - bits); }

static void install_hooks(const struct mach_header *mh, intptr_t slide) {
    (void)slide;
    unsigned long size = 0;
    uint8_t *text = getsectiondata((const struct mach_header_64 *)mh, "__TEXT", "__text", &size);
    if (!text) return;
    const uint32_t *w = (const uint32_t *)text;
    size_t n = size / 4;
    const uint32_t mark0 = 0xD2800000 | 0x5552u << 5 | 17, mark1 = 0xF2A00000 | 0x484Bu << 5 | 17;
    int installed = 0;
    for (size_t i = 0; i + 5 < n; i++) {
        if (w[i] != mark0 || w[i + 1] != mark1) continue;
        uint32_t id = (w[i + 2] >> 5) & 0xFFFF;
        uint32_t adrp = w[i + 3], ldr = w[i + 4];
        if ((adrp & 0x9F00001F) != 0x90000010 || (ldr & 0xFFC003FF) != 0xF9400210) continue;
        uintptr_t pc = (uintptr_t)&w[i + 3];
        int64_t pages = sign_extend(((adrp >> 29) & 3) | ((adrp >> 5) & 0x7FFFF) << 2, 21);
        uintptr_t slot = (pc & ~(uintptr_t)0xFFF) + (uintptr_t)(pages << 12) + ((ldr >> 10) & 0xFFF) * 8;
        if (id < sizeof(g_hooks) / sizeof(g_hooks[0]) && g_hooks[id]) {
            *(void *volatile *)slot = g_hooks[id];
            installed++;
        }
    }
    bpp_log("engine hooks installed: %d", installed);
}

static void on_add_image(const struct mach_header *mh, intptr_t slide) {
    for (uint32_t i = 0; i < _dyld_image_count(); i++) {
        if (_dyld_get_image_header(i) != mh) continue;
        const char *name = _dyld_get_image_name(i);
        if (name && strstr(name, "/UnityRuntime.framework/UnityRuntime")) install_hooks(mh, slide);
        return;
    }
}

__attribute__((constructor)) static void engine_hooks_init(void) {
    _dyld_register_func_for_add_image(on_add_image);
}
#endif
