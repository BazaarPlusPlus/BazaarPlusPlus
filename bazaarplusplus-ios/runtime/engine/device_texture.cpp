// The archive preparation renames only the original definition. Existing engine
// references resolve to this wrapper at link time, before device signing.
#include <stdint.h>
enum GraphicsFormat : int;
enum TextureFormat : int;
extern GraphicsFormat GetLinearFormat(GraphicsFormat);
extern GraphicsFormat GetGraphicsFormat_Native_TextureFormat(TextureFormat, bool);
extern "C" bool bpp_engine_decompress_original(GraphicsFormat, GraphicsFormat, int, int, const uint32_t*, int, int, uint32_t*);
extern "C" bool bpp_decompress_native_texture(int, int, int, int, const uint8_t*, int, int, uint8_t*);
bool DecompressNativeTexture(GraphicsFormat src, GraphicsFormat dst, int width, int height,
                             const uint32_t *input, int dstWidth, int dstHeight, uint32_t *output) {
    int linear = (int)GetLinearFormat(src);
    if (linear >= 97 && linear <= 109)
        return bpp_decompress_native_texture(linear, (int)GetLinearFormat(dst), width, height,
            (const uint8_t*)input, dstWidth, dstHeight, (uint8_t*)output);
    return bpp_engine_decompress_original(src, dst, width, height, input, dstWidth, dstHeight, output);
}
bool DecompressNativeTexture(TextureFormat src, TextureFormat dst, int width, int height,
                             const uint32_t *input, int dstWidth, int dstHeight, uint32_t *output) {
    return DecompressNativeTexture(GetGraphicsFormat_Native_TextureFormat(src, false),
        GetGraphicsFormat_Native_TextureFormat(dst, false), width, height, input, dstWidth, dstHeight, output);
}
