package com.bazaarplusplus.android.payload;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Pattern;

/**
 * Canonical parser for {@code bpp-managed-payload-chunks 1}, owned by {@code release/managed-payload-chunks.ts}
 * and pinned by {@code release/fixtures/managed-payload/chunks.txt}.
 */
public final class ManagedPayloadChunkIndex {
    public static final String HEADER = "bpp-managed-payload-chunks 1";
    public static final String MANIFEST_HEADER = "bpp-managed-payload-manifest 1";
    public static final String COMPRESSION = "deflate-raw";
    public static final int MAX_INDEX_BYTES = 4 * 1024 * 1024;
    public static final int MAX_MANIFEST_BYTES = 1024 * 1024;
    public static final int MAX_MANIFEST_SIGNATURE_BYTES = 16 * 1024;
    public static final int MAX_COMPRESSED_OVERHEAD = 1024;
    public static final int MAX_CHUNK_COUNT = 65536;
    public static final int MAX_FILE_COUNT = 4096;
    public static final int MAX_CHUNK_REFERENCES = 131072;

    private static final Pattern SHA256 = Pattern.compile("[0-9a-f]{64}");
    private static final Pattern VERSION = Pattern.compile("(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)");
    private static final Pattern PLATFORM = Pattern.compile("[A-Za-z0-9_-]+");
    private static final Pattern STRICT_INT = Pattern.compile("0|[1-9][0-9]*");

    public static final class ChunkRecord {
        public final String rawSha256;
        public final int rawSize;
        public final String compressedSha256;
        public final int compressedSize;

        ChunkRecord(String rawSha256, int rawSize, String compressedSha256, int compressedSize) {
            this.rawSha256 = rawSha256;
            this.rawSize = rawSize;
            this.compressedSha256 = compressedSha256;
            this.compressedSize = compressedSize;
        }
    }

    public static final class FileRecord {
        public final String path;
        public final long size;
        public final String sha256;
        public final int[] chunks;

        FileRecord(String path, long size, String sha256, int[] chunks) {
            this.path = path;
            this.size = size;
            this.sha256 = sha256;
            this.chunks = chunks;
        }
    }

    private static final class ManifestFile {
        final String path;
        final long size;
        final String sha256;

        ManifestFile(String path, long size, String sha256) {
            this.path = path;
            this.size = size;
            this.sha256 = sha256;
        }
    }

    public final String version;
    public final String platform;
    public final FastCdcChunker.Params chunking;
    public final byte[] manifestBytes;
    public final byte[] manifestSignatureBytes;
    public final List<ChunkRecord> chunks;
    public final List<FileRecord> files;

    private ManagedPayloadChunkIndex(String version, String platform, FastCdcChunker.Params chunking,
            byte[] manifestBytes, byte[] manifestSignatureBytes, List<ChunkRecord> chunks, List<FileRecord> files) {
        this.version = version;
        this.platform = platform;
        this.chunking = chunking;
        this.manifestBytes = manifestBytes;
        this.manifestSignatureBytes = manifestSignatureBytes;
        this.chunks = Collections.unmodifiableList(chunks);
        this.files = Collections.unmodifiableList(files);
    }

    public static ManagedPayloadChunkIndex parse(byte[] bytes) throws IOException {
        if (bytes == null || bytes.length == 0 || bytes.length > MAX_INDEX_BYTES) {
            throw new IOException("Chunk index size out of bounds");
        }
        return parse(new String(bytes, StandardCharsets.UTF_8));
    }

    public static ManagedPayloadChunkIndex parse(String text) throws IOException {
        if (text == null || !text.endsWith("\n") || text.indexOf('\r') >= 0) {
            throw new IOException("Chunk index must be LF-terminated lines");
        }
        if (text.getBytes(StandardCharsets.UTF_8).length > MAX_INDEX_BYTES) {
            throw new IOException("Chunk index exceeds size limit");
        }
        String[] lines = text.substring(0, text.length() - 1).split("\n", -1);
        if (lines.length < 8 || !HEADER.equals(lines[0])) {
            throw new IOException("Not a Managed Payload Chunk Index");
        }

        String version = field(lines[1], "version");
        if (!VERSION.matcher(version).matches()) throw new IOException("Invalid chunk index version " + version);
        String platform = field(lines[2], "platform");
        if (!PLATFORM.matcher(platform).matches()) throw new IOException("Invalid chunk index platform " + platform);

        String[] chunkingParts = field(lines[3], "chunking").split(" ", -1);
        if (chunkingParts.length != 4 || !FastCdcChunker.ALGORITHM.equals(chunkingParts[0])) {
            throw new IOException("Unsupported chunking header");
        }
        FastCdcChunker.Params chunking = new FastCdcChunker.Params(
                parseStrictInt(chunkingParts[1], "minSize"),
                parseStrictInt(chunkingParts[2], "avgSize"),
                parseStrictInt(chunkingParts[3], "maxSize"));
        try {
            chunking.validateBits();
        } catch (IllegalArgumentException e) {
            throw new IOException("Unsupported FastCDC parameters", e);
        }

        if (!COMPRESSION.equals(field(lines[4], "compression"))) {
            throw new IOException("Unsupported chunk compression");
        }

        byte[] manifestBytes = decodeStrictBase64(field(lines[5], "manifest-base64"), MAX_MANIFEST_BYTES);
        byte[] manifestSignatureBytes = decodeStrictBase64(field(lines[6], "manifest-sig-base64"),
                MAX_MANIFEST_SIGNATURE_BYTES);
        String signatureText = new String(manifestSignatureBytes, StandardCharsets.UTF_8);
        if (signatureText.trim().isEmpty()) throw new IOException("Empty embedded manifest signature");

        List<ManifestFile> manifestFiles = parseEmbeddedManifest(
                new String(manifestBytes, StandardCharsets.UTF_8), version, platform);

        List<ChunkRecord> chunks = new ArrayList<>();
        Set<String> seenRawSha = new HashSet<>();
        List<FileRecord> files = new ArrayList<>();
        boolean inFiles = false;
        int totalRefs = 0;

        for (int i = 7; i < lines.length; i++) {
            String line = lines[i];
            if (line.startsWith("chunk ")) {
                if (inFiles) throw new IOException("Chunk line after file lines at " + (i + 1));
                if (chunks.size() >= MAX_CHUNK_COUNT) throw new IOException("Too many chunks");
                String[] parts = line.split(" ", -1);
                if (parts.length != 5) throw new IOException("Invalid chunk line " + (i + 1));
                String rawSha = parts[1];
                int rawSize = parseStrictInt(parts[2], "rawSize");
                String compSha = parts[3];
                int compSize = parseStrictInt(parts[4], "compressedSize");
                if (!SHA256.matcher(rawSha).matches() || !SHA256.matcher(compSha).matches()
                        || rawSize <= 0 || rawSize > chunking.maxSize
                        || compSize <= 0 || compSize > chunking.maxSize + MAX_COMPRESSED_OVERHEAD) {
                    throw new IOException("Invalid chunk metadata at line " + (i + 1));
                }
                if (!seenRawSha.add(rawSha)) throw new IOException("Duplicate chunk rawSha256 at line " + (i + 1));
                chunks.add(new ChunkRecord(rawSha, rawSize, compSha, compSize));
                continue;
            }
            if (line.startsWith("file ")) {
                inFiles = true;
                if (files.size() >= MAX_FILE_COUNT) throw new IOException("Too many files");
                String[] parts = line.split(" ", 5);
                if (parts.length != 5 || !"file".equals(parts[0]) || !SHA256.matcher(parts[1]).matches()) {
                    throw new IOException("Invalid file line " + (i + 1));
                }
                long fileSize = parseStrictLong(parts[2], "fileSize");
                String refsToken = parts[3];
                String filePath = parts[4];
                if (filePath.isEmpty()) throw new IOException("Empty file path at line " + (i + 1));

                int[] refs;
                if ("-".equals(refsToken)) {
                    if (fileSize != 0) throw new IOException("Non-empty file has no chunks at line " + (i + 1));
                    refs = new int[0];
                } else {
                    String[] tokens = refsToken.split(",", -1);
                    totalRefs += tokens.length;
                    if (totalRefs > MAX_CHUNK_REFERENCES) throw new IOException("Too many chunk references");
                    refs = new int[tokens.length];
                    long sum = 0;
                    for (int t = 0; t < tokens.length; t++) {
                        int idx = parseStrictInt(tokens[t], "chunkIndex");
                        if (idx >= chunks.size()) throw new IOException("Chunk index out of range at line " + (i + 1));
                        refs[t] = idx;
                        sum += chunks.get(idx).rawSize;
                    }
                    if (sum != fileSize) throw new IOException("File chunk size sum mismatch at line " + (i + 1));
                }
                files.add(new FileRecord(filePath, fileSize, parts[1], refs));
                continue;
            }
            throw new IOException("Unexpected chunk index line " + (i + 1));
        }

        if (files.size() != manifestFiles.size()) {
            throw new IOException("Chunk index file count differs from embedded manifest");
        }
        for (int i = 0; i < files.size(); i++) {
            FileRecord file = files.get(i);
            ManifestFile expected = manifestFiles.get(i);
            if (!file.path.equals(expected.path) || file.size != expected.size || !file.sha256.equals(expected.sha256)) {
                throw new IOException("Chunk index file " + file.path + " differs from embedded manifest");
            }
        }
        return new ManagedPayloadChunkIndex(version, platform, chunking, manifestBytes, manifestSignatureBytes,
                chunks, files);
    }

    private static List<ManifestFile> parseEmbeddedManifest(String text, String expectedVersion,
            String expectedPlatform) throws IOException {
        if (!text.endsWith("\n") || text.indexOf('\r') >= 0) {
            throw new IOException("Embedded manifest must be LF-terminated");
        }
        String[] lines = text.substring(0, text.length() - 1).split("\n", -1);
        if (lines.length < 6 || !MANIFEST_HEADER.equals(lines[0])) {
            throw new IOException("Invalid embedded Managed Payload Manifest");
        }
        String version = field(lines[1], "version");
        if (!version.equals(expectedVersion)) throw new IOException("Embedded manifest version mismatch");
        String platform = field(lines[2], "platform");
        if (!platform.equals(expectedPlatform)) throw new IOException("Embedded manifest platform mismatch");
        long historyVersion = parseStrictLong(field(lines[3], "history-database-user-version"), "historyVersion");
        if (historyVersion <= 0) throw new IOException("Invalid embedded manifest history version");
        String nonManaged = field(lines[4], "non-managed-payload-digest");
        if (!SHA256.matcher(nonManaged).matches()) throw new IOException("Invalid embedded non-managed digest");

        List<ManifestFile> files = new ArrayList<>();
        String previous = null;
        for (int i = 5; i < lines.length; i++) {
            String[] parts = lines[i].split(" ", 4);
            if (parts.length != 4 || !"file".equals(parts[0]) || !SHA256.matcher(parts[1]).matches()) {
                throw new IOException("Invalid embedded manifest file line " + (i + 1));
            }
            long size = parseStrictLong(parts[2], "manifestFileSize");
            String path = parts[3];
            if (path.isEmpty() || (previous != null && previous.compareTo(path) >= 0)) {
                throw new IOException("Embedded manifest files must be sorted and unique");
            }
            previous = path;
            files.add(new ManifestFile(path, size, parts[1]));
        }
        if (files.isEmpty()) throw new IOException("Embedded manifest names no files");
        return files;
    }

    private static String field(String line, String name) throws IOException {
        String prefix = name + " ";
        if (!line.startsWith(prefix)) throw new IOException("Missing field " + name);
        return line.substring(prefix.length());
    }

    private static byte[] decodeStrictBase64(String value, int maxBytes) throws IOException {
        if (!isStrictBase64(value)) throw new IOException("Invalid base64");
        int maxPossible = (value.length() / 4) * 3;
        if (maxPossible - 2 > maxBytes) throw new IOException("Base64 field exceeds size limit");
        byte[] decoded;
        try {
            decoded = Base64.getDecoder().decode(value);
        } catch (IllegalArgumentException e) {
            throw new IOException("Invalid base64", e);
        }
        if (decoded.length == 0 || decoded.length > maxBytes) throw new IOException("Decoded base64 out of bounds");
        if (!Base64.getEncoder().encodeToString(decoded).equals(value)) {
            throw new IOException("Non-canonical base64");
        }
        return decoded;
    }

    private static boolean isStrictBase64(String value) {
        if (value == null || value.isEmpty() || (value.length() % 4) != 0) return false;
        int padding = 0;
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            if (c == '=') {
                padding++;
                if (padding > 2 || i < value.length() - 2) return false;
                continue;
            }
            if (padding > 0) return false;
            boolean ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                    || c == '+' || c == '/';
            if (!ok) return false;
        }
        return true;
    }

    private static int parseStrictInt(String value, String label) throws IOException {
        if (!STRICT_INT.matcher(value).matches()) throw new IOException("Invalid integer " + label);
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException e) {
            throw new IOException("Integer overflow " + label, e);
        }
    }

    private static long parseStrictLong(String value, String label) throws IOException {
        if (!STRICT_INT.matcher(value).matches()) throw new IOException("Invalid long " + label);
        try {
            return Long.parseLong(value);
        } catch (NumberFormatException e) {
            throw new IOException("Long overflow " + label, e);
        }
    }
}
