package com.bazaarplusplus.android.payload;

import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Arrays;

/**
 * Deterministic 32-bit Gear FastCDC matching {@code release/managed-payload-chunks.ts} ({@code fastcdc-gear32-v1})
 * and pinned by {@code release/fixtures/managed-payload/cdc-vectors.json}.
 */
public final class FastCdcChunker {
    public static final String ALGORITHM = "fastcdc-gear32-v1";
    public static final int WARMUP_BYTES = 32;
    public static final int MIN_BITS = 10;
    public static final int MAX_BITS = 18;
    public static final int MAX_ALLOWED_CHUNK_SIZE = 256 * 1024;

    private static final int[] GEAR = buildGearTable();

    private FastCdcChunker() {}

    public static final class Params {
        public final int minSize;
        public final int avgSize;
        public final int maxSize;

        public Params(int minSize, int avgSize, int maxSize) {
            this.minSize = minSize;
            this.avgSize = avgSize;
            this.maxSize = maxSize;
        }

        public int validateBits() {
            if (minSize < WARMUP_BYTES) {
                throw new IllegalArgumentException("minSize must be at least " + WARMUP_BYTES);
            }
            if (avgSize <= 0 || (avgSize & (avgSize - 1)) != 0) {
                throw new IllegalArgumentException("avgSize must be a power of two");
            }
            int bits = Integer.numberOfTrailingZeros(avgSize);
            if (bits < MIN_BITS || bits > MAX_BITS) {
                throw new IllegalArgumentException("avgSize bits out of range: " + bits);
            }
            if (minSize >= avgSize || avgSize >= maxSize || maxSize > MAX_ALLOWED_CHUNK_SIZE) {
                throw new IllegalArgumentException("Invalid FastCDC size bounds");
            }
            return bits;
        }
    }

    static long gearUnsigned(int index) {
        return GEAR[index] & 0xFFFFFFFFL;
    }

    private static int[] buildGearTable() {
        MessageDigest sha;
        try {
            sha = MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
        int[] table = new int[256];
        byte[] input = new byte[1];
        for (int i = 0; i < 256; i++) {
            input[0] = (byte) i;
            byte[] digest = sha.digest(input);
            table[i] = (digest[0] & 0xFF)
                    | ((digest[1] & 0xFF) << 8)
                    | ((digest[2] & 0xFF) << 16)
                    | ((digest[3] & 0xFF) << 24);
        }
        return table;
    }

    public static int[] chunkBoundaries(byte[] data, int length, Params params) {
        if (data == null) throw new IllegalArgumentException("data is null");
        if (length < 0 || length > data.length) throw new IllegalArgumentException("length out of bounds");
        int bits = params.validateBits();
        if (length == 0) return new int[0];

        int minSize = params.minSize;
        int avgSize = params.avgSize;
        int maxSize = params.maxSize;
        int maskS = ~0 << (32 - (bits + 1));
        int maskL = ~0 << (32 - (bits - 1));

        int[] boundaries = new int[Math.max(8, length / avgSize + 4)];
        int count = 0;
        int pos = 0;
        while (pos < length) {
            int cut;
            if (length - pos <= minSize) {
                cut = length;
            } else {
                int end = Math.min(pos + maxSize, length);
                int normal = Math.min(pos + avgSize, end);
                int fp = 0;
                int firstTest = pos + minSize - 1;
                for (int w = pos + minSize - WARMUP_BYTES; w < firstTest; w++) {
                    fp = (fp << 1) + GEAR[data[w] & 0xFF];
                }
                cut = end;
                int i = firstTest;
                while (i < normal) {
                    fp = (fp << 1) + GEAR[data[i] & 0xFF];
                    if ((fp & maskS) == 0) {
                        cut = i + 1;
                        break;
                    }
                    i++;
                }
                if (cut == end) {
                    while (i < end) {
                        fp = (fp << 1) + GEAR[data[i] & 0xFF];
                        if ((fp & maskL) == 0) {
                            cut = i + 1;
                            break;
                        }
                        i++;
                    }
                }
            }
            if (count == boundaries.length) boundaries = Arrays.copyOf(boundaries, boundaries.length * 2);
            boundaries[count++] = cut;
            pos = cut;
        }
        return Arrays.copyOf(boundaries, count);
    }
}
