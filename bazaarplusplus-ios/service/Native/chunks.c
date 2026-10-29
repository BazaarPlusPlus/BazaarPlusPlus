// Steam container framing adapted from SteamRE/SteamKit (LGPL-2.1-only).
// See service/Managed/THIRD_PARTY.md for the pinned source and attribution.
#include <CommonCrypto/CommonCryptor.h>
#include <CommonCrypto/CommonDigest.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <zlib.h>
#include "zstd.h"
#define API __attribute__((visibility("default"), used))
API int bpp_steam_file_sha1(const char *path, uint8_t *digest) {
    FILE *file = fopen(path, "rb");
    if (!file) return -1;
    CC_SHA1_CTX context; CC_SHA1_Init(&context);
    uint8_t buffer[65536]; size_t count;
    while ((count = fread(buffer, 1, sizeof(buffer), file)) != 0)
        CC_SHA1_Update(&context, buffer, (CC_LONG)count);
    int failed = ferror(file); fclose(file);
    if (failed) return -1;
    CC_SHA1_Final(digest, &context);
    return 0;
}
API int bpp_steam_decrypt(const uint8_t *input, int count, const uint8_t *key, uint8_t *output) {
    if (count < 32 || count % 16) return -1;
    uint8_t iv[16]; size_t length = 0;
    if (CCCrypt(kCCDecrypt, kCCAlgorithmAES, kCCOptionECBMode, key, 32, NULL, input, 16, iv, 16, &length) != kCCSuccess) return -1;
    if (CCCrypt(kCCDecrypt, kCCAlgorithmAES, kCCOptionPKCS7Padding, key, 32, iv, input + 16, count - 16, output, count, &length) != kCCSuccess) return -1;
    return (int)length;
}
static uint32_t le32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1]<<8 | (uint32_t)p[2]<<16 | (uint32_t)p[3]<<24; }
API int bpp_steam_zstd(const uint8_t *input, int count, uint8_t *output, int capacity) {
    if (count < 23 || memcmp(input, "VSZa", 4) || memcmp(input + count - 3, "zsv", 3)) return -1;
    uint32_t crc = le32(input + 4), expected = le32(input + count - 11);
    if (crc != le32(input + count - 15) || expected != (uint32_t)capacity) return -1;
    size_t size = ZSTD_decompress(output, capacity, input + 8, count - 23);
    if (ZSTD_isError(size) || size != expected || crc32(0, output, (uInt)size) != crc) return -1;
    return (int)size;
}
