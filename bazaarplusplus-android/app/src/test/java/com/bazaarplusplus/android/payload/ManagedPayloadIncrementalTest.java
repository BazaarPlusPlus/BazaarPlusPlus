package com.bazaarplusplus.android.payload;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.io.Sha256;
import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.PlatformManifest;
import com.bazaarplusplus.android.release.ReleaseOrigin;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Base64;
import java.util.Collections;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.zip.Deflater;

public class ManagedPayloadIncrementalTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    private static final File CDC_VECTORS = new File("../../release/fixtures/managed-payload/cdc-vectors.json");
    private static final File CHUNKS_GOLDEN = new File("../../release/fixtures/managed-payload/chunks.txt");

    @Test
    public void sharedFastCdcGoldenVectorsMatchByteForByte() throws Exception {
        JSONObject doc = new JSONObject(new String(Files.readAllBytes(CDC_VECTORS.toPath()), StandardCharsets.UTF_8));
        assertEquals(FastCdcChunker.ALGORITHM, doc.getString("algorithm"));

        JSONArray samples = doc.getJSONArray("gearSamples");
        for (int i = 0; i < samples.length(); i++) {
            JSONObject sample = samples.getJSONObject(i);
            assertEquals(sample.getLong("value"), FastCdcChunker.gearUnsigned(sample.getInt("index")));
        }

        JSONArray cases = doc.getJSONArray("cases");
        for (int c = 0; c < cases.length(); c++) {
            JSONObject tc = cases.getJSONObject(c);
            String name = tc.getString("name");
            JSONObject p = tc.getJSONObject("params");
            FastCdcChunker.Params params = new FastCdcChunker.Params(
                    p.getInt("minSize"), p.getInt("avgSize"), p.getInt("maxSize"));
            byte[] prefix = hexToBytes(tc.getString("prefixHex"));
            byte[] body = prngBytes((int) tc.getLong("seed"), tc.getInt("length"));
            byte[] data = new byte[prefix.length + body.length];
            System.arraycopy(prefix, 0, data, 0, prefix.length);
            System.arraycopy(body, 0, data, prefix.length, body.length);

            assertEquals(name + " dataSha256", tc.getString("dataSha256"), Sha256.of(data));

            int[] actual = FastCdcChunker.chunkBoundaries(data, data.length, params);
            JSONArray expectedArr = tc.getJSONArray("boundaries");
            int[] expected = new int[expectedArr.length()];
            for (int i = 0; i < expected.length; i++) expected[i] = expectedArr.getInt(i);
            assertArrayEquals(name + " boundaries", expected, actual);

            JSONArray expectedHashes = tc.getJSONArray("chunkSha256s");
            int start = 0;
            for (int i = 0; i < actual.length; i++) {
                String actualHash = Sha256.of(data, start, actual[i] - start);
                assertEquals(name + " chunk " + i, expectedHashes.getString(i), actualHash);
                start = actual[i];
            }
        }
    }

    @Test
    public void sharedChunkIndexGoldenParsesCleanly() throws Exception {
        byte[] bytes = Files.readAllBytes(CHUNKS_GOLDEN.toPath());
        ManagedPayloadChunkIndex parsed = ManagedPayloadChunkIndex.parse(bytes);
        assertEquals("6.1.0", parsed.version);
        assertEquals("darwin-aarch64", parsed.platform);
        assertEquals(512, parsed.chunking.minSize);
        assertEquals(2048, parsed.chunking.avgSize);
        assertEquals(8192, parsed.chunking.maxSize);
        assertEquals(3, parsed.files.size());
        assertEquals("BepInEx/plugins/Empty.txt", parsed.files.get(1).path);
        assertEquals(0, parsed.files.get(1).chunks.length);
        assertEquals("BepInEx/plugins/Sub File.dll", parsed.files.get(2).path);
    }

    @Test
    public void chunkIndexParserRejectsMalformedInputs() throws Exception {
        String valid = new String(Files.readAllBytes(CHUNKS_GOLDEN.toPath()), StandardCharsets.UTF_8);
        assertThrows(IOException.class, () -> ManagedPayloadChunkIndex.parse(valid.replace("\n", "\r\n")));
        assertThrows(IOException.class, () -> ManagedPayloadChunkIndex.parse(valid.substring(0, valid.length() - 1)));
        assertThrows(IOException.class, () -> ManagedPayloadChunkIndex.parse(
                valid.replace("bpp-managed-payload-chunks 1", "bpp-managed-payload-chunks 2")));
        assertThrows(IOException.class, () -> ManagedPayloadChunkIndex.parse(
                valid.replace("fastcdc-gear32-v1 512 2048 8192", "fastcdc-gear32-v1 16 2048 8192")));
        assertThrows(IOException.class, () -> ManagedPayloadChunkIndex.parse(
                valid.replace("compression deflate-raw", "compression zstd")));
    }

    @Test
    public void inflateRawRejectsDecompressionBombAndTruncatedStream() throws Exception {
        byte[] raw = new byte[4096];
        Arrays.fill(raw, (byte) 'A');
        byte[] compressed = deflateRaw(raw);
        assertArrayEquals(raw, ManagedPayloadIncremental.inflateRaw(compressed, raw.length));
        assertThrows(IOException.class, () -> ManagedPayloadIncremental.inflateRaw(compressed, 512));
        assertThrows(IOException.class, () -> ManagedPayloadIncremental.inflateRaw(compressed, 8192));
    }

    @Test
    public void incrementalStagesOnlyChangedChunksAndDefersRemoveInstalledUntilVerified() throws Exception {
        byte[] sharedLib = prngBytes(11, 48 * 1024);
        byte[] coreOld = prngBytes(22, 32 * 1024);
        byte[] coreNew = coreOld.clone();
        byte[] patch = prngBytes(99, 4 * 1024);
        System.arraycopy(patch, 0, coreNew, coreNew.length - patch.length, patch.length);

        File external = tmp.newFolder("external");
        File updates = new File(external, "BazaarPlusPlusV6/Updates");
        writeInstalled(external, "6.4.4", Map.of(
                "MelonLoader/UserLibs/Shared.dll", sharedLib,
                "MelonLoader/Mods/BazaarPlusPlus.Core.dll", coreOld,
                "MelonLoader/Mods/Removed.dll", "gone".getBytes(StandardCharsets.UTF_8)));

        Map<String, byte[]> targetFiles = new TreeMap<>();
        targetFiles.put("MelonLoader/UserLibs/Shared.dll", sharedLib);
        targetFiles.put("MelonLoader/Mods/BazaarPlusPlus.Core.dll", coreNew);
        targetFiles.put("MelonLoader/Mods/Empty.txt", new byte[0]);
        FixtureRelease bundle = buildBundle("6.4.5", targetFiles, 80 * 1024);

        List<String> requestedUrls = new CopyOnWriteArrayList<>();
        List<long[]> progressUpdates = new CopyOnWriteArrayList<>();
        ManagedPayloadIncremental.Outcome outcome = ManagedPayloadIncremental.tryStage(
                bundle.managedPayload, "6.4.5", external, updates, true,
                (url, identity) -> {
                    requestedUrls.add(url);
                    return bundle.open(url);
                },
                (received, total) -> progressUpdates.add(new long[] {received, total}));

        assertTrue("expected incremental stage, got " + outcome.fallbackReason, outcome.staged);
        assertNull(outcome.fallbackReason);
        assertTrue(outcome.downloadedBytes < bundle.managedPayload.file.size);
        long chunkFetches = requestedUrls.stream().filter(u -> u.endsWith(".deflate")).count();
        assertTrue("fetched " + chunkFetches + " of " + bundle.chunks.size(),
                chunkFetches > 0 && chunkFetches < bundle.chunks.size() / 2);
        assertFalse(progressUpdates.isEmpty());
        // Old installed files were deleted because removeInstalledOnPublish=true.
        assertFalse(new File(external, "MelonLoader/Mods/Removed.dll").exists());
        assertFalse(new File(external, ManagedPayloadStaging.INSTALLED_MANIFEST).exists());

        File stagedDir = new File(updates, "6.4.5");
        assertTrue(stagedDir.isDirectory());
        assertFalse(new File(stagedDir, ".chunks.tmp").exists());
        assertArrayEquals(sharedLib,
                Files.readAllBytes(new File(stagedDir, "files/MelonLoader/UserLibs/Shared.dll").toPath()));
        assertArrayEquals(coreNew,
                Files.readAllBytes(new File(stagedDir, "files/MelonLoader/Mods/BazaarPlusPlus.Core.dll").toPath()));
        assertArrayEquals(new byte[0],
                Files.readAllBytes(new File(stagedDir, "files/MelonLoader/Mods/Empty.txt").toPath()));
        assertTrue(new File(stagedDir, "manifest.txt").isFile());
        assertTrue(new File(stagedDir, "manifest.txt.minisig").isFile());

        // Second run seeds from Updates/6.4.5/files/ and fetches 0 chunks!
        requestedUrls.clear();
        ManagedPayloadIncremental.Outcome retry = ManagedPayloadIncremental.tryStage(
                bundle.managedPayload, "6.4.5", external, updates, false,
                (url, identity) -> {
                    requestedUrls.add(url);
                    return bundle.open(url);
                },
                null);
        assertTrue(retry.staged);
        assertEquals(0, requestedUrls.stream().filter(u -> u.endsWith(".deflate")).count());
    }

    @Test
    public void incrementalFailureLeavesInstalledModsIntactAndReturnsFallback() throws Exception {
        byte[] sharedLib = prngBytes(11, 32 * 1024);
        byte[] coreOld = prngBytes(22, 16 * 1024);
        byte[] coreNew = coreOld.clone();
        coreNew[coreNew.length - 1] ^= (byte) 0xff;

        File external = tmp.newFolder("external-fail");
        File updates = new File(external, "BazaarPlusPlusV6/Updates");
        writeInstalled(external, "6.4.4", Map.of(
                "MelonLoader/UserLibs/Shared.dll", sharedLib,
                "MelonLoader/Mods/BazaarPlusPlus.Core.dll", coreOld));

        Map<String, byte[]> targetFiles = new TreeMap<>();
        targetFiles.put("MelonLoader/UserLibs/Shared.dll", sharedLib);
        targetFiles.put("MelonLoader/Mods/BazaarPlusPlus.Core.dll", coreNew);
        FixtureRelease bundle = buildBundle("6.4.5", targetFiles, 50 * 1024);

        // Corrupt chunk delivery -> fallback without deleting installed mods!
        ManagedPayloadIncremental.Outcome failed = ManagedPayloadIncremental.tryStage(
                bundle.managedPayload, "6.4.5", external, updates, true,
                (url, identity) -> {
                    if (url.endsWith(".deflate")) {
                        return () -> new ByteArrayInputStream("bad".getBytes(StandardCharsets.UTF_8));
                    }
                    return bundle.open(url);
                },
                null);
        assertFalse(failed.staged);
        assertEquals("size_mismatch", failed.fallbackReason);
        assertTrue("installed Mods must stay untouched on incremental failure",
                new File(external, "MelonLoader/Mods/BazaarPlusPlus.Core.dll").isFile());
        assertTrue(new File(external, ManagedPayloadStaging.INSTALLED_MANIFEST).isFile());
        String[] left = updates.list();
        assertTrue(left == null || left.length == 0);
    }

    @Test
    public void thresholdExceededWithoutSeedsFallsBackWithoutFetchingChunks() throws Exception {
        File external = tmp.newFolder("external-empty");
        File updates = new File(external, "BazaarPlusPlusV6/Updates");
        Map<String, byte[]> targetFiles = new TreeMap<>();
        targetFiles.put("MelonLoader/Mods/BazaarPlusPlus.Core.dll", prngBytes(44, 24 * 1024));
        FixtureRelease bundle = buildBundle("6.4.5", targetFiles, 24 * 1024);

        List<String> requested = new CopyOnWriteArrayList<>();
        ManagedPayloadIncremental.Outcome outcome = ManagedPayloadIncremental.tryStage(
                bundle.managedPayload, "6.4.5", external, updates, false,
                (url, identity) -> {
                    requested.add(url);
                    return bundle.open(url);
                },
                null);
        assertFalse(outcome.staged);
        assertEquals("threshold_exceeded", outcome.fallbackReason);
        assertEquals(0, requested.stream().filter(u -> u.endsWith(".deflate")).count());
    }

    private static void writeInstalled(File external, String version, Map<String, byte[]> files) throws IOException {
        Map<String, byte[]> sorted = new TreeMap<>(files);
        StringBuilder manifest = new StringBuilder("bpp-managed-payload-manifest 1\nversion ").append(version)
                .append("\nplatform android-aarch64\nhistory-database-user-version 1\nnon-managed-payload-digest ")
                .append("e".repeat(64)).append('\n');
        for (Map.Entry<String, byte[]> entry : sorted.entrySet()) {
            File target = new File(external, entry.getKey());
            assertTrue(target.getParentFile().isDirectory() || target.getParentFile().mkdirs());
            Files.write(target.toPath(), entry.getValue());
            manifest.append("file ").append(Sha256.of(entry.getValue())).append(' ')
                    .append(entry.getValue().length).append(' ').append(entry.getKey()).append('\n');
        }
        File manifestFile = new File(external, ManagedPayloadStaging.INSTALLED_MANIFEST);
        assertTrue(manifestFile.getParentFile().isDirectory() || manifestFile.getParentFile().mkdirs());
        Files.write(manifestFile.toPath(), manifest.toString().getBytes(StandardCharsets.UTF_8));
    }

    private static FixtureRelease buildBundle(String version, Map<String, byte[]> files, long fullZipSize)
            throws Exception {
        Map<String, byte[]> sorted = new TreeMap<>(files);
        StringBuilder manifest = new StringBuilder("bpp-managed-payload-manifest 1\nversion ").append(version)
                .append("\nplatform android-aarch64\nhistory-database-user-version 1\nnon-managed-payload-digest ")
                .append("e".repeat(64)).append('\n');
        for (Map.Entry<String, byte[]> entry : sorted.entrySet()) {
            manifest.append("file ").append(Sha256.of(entry.getValue())).append(' ')
                    .append(entry.getValue().length).append(' ').append(entry.getKey()).append('\n');
        }
        byte[] manifestBytes = manifest.toString().getBytes(StandardCharsets.UTF_8);
        byte[] sigBytes = "untrusted comment: test\nRW...\n".getBytes(StandardCharsets.UTF_8);

        FastCdcChunker.Params params = new FastCdcChunker.Params(512, 2048, 8192);
        List<String> chunkLines = new ArrayList<>();
        Map<String, Integer> rawIdx = new HashMap<>();
        Map<String, byte[]> compressedChunks = new LinkedHashMap<>();
        List<String> fileLines = new ArrayList<>();

        for (Map.Entry<String, byte[]> entry : sorted.entrySet()) {
            byte[] content = entry.getValue();
            int[] bounds = FastCdcChunker.chunkBoundaries(content, content.length, params);
            StringBuilder refs = new StringBuilder();
            int start = 0;
            for (int end : bounds) {
                byte[] raw = Arrays.copyOfRange(content, start, end);
                String rawSha = Sha256.of(raw);
                Integer idx = rawIdx.get(rawSha);
                if (idx == null) {
                    byte[] comp = deflateRaw(raw);
                    String compSha = Sha256.of(comp);
                    idx = chunkLines.size();
                    rawIdx.put(rawSha, idx);
                    chunkLines.add("chunk " + rawSha + " " + raw.length + " " + compSha + " " + comp.length);
                    compressedChunks.put(compSha, comp);
                }
                if (refs.length() > 0) refs.append(',');
                refs.append(idx);
                start = end;
            }
            fileLines.add("file " + Sha256.of(content) + " " + content.length + " "
                    + (refs.length() == 0 ? "-" : refs.toString()) + " " + entry.getKey());
        }

        StringBuilder idxText = new StringBuilder(ManagedPayloadChunkIndex.HEADER).append('\n')
                .append("version ").append(version).append('\n')
                .append("platform android-aarch64\n")
                .append("chunking ").append(FastCdcChunker.ALGORITHM).append(' ')
                .append(params.minSize).append(' ').append(params.avgSize).append(' ').append(params.maxSize).append('\n')
                .append("compression ").append(ManagedPayloadChunkIndex.COMPRESSION).append('\n')
                .append("manifest-base64 ").append(Base64.getEncoder().encodeToString(manifestBytes)).append('\n')
                .append("manifest-sig-base64 ").append(Base64.getEncoder().encodeToString(sigBytes)).append('\n');
        for (String line : chunkLines) idxText.append(line).append('\n');
        for (String line : fileLines) idxText.append(line).append('\n');
        byte[] indexBytes = idxText.toString().getBytes(StandardCharsets.UTF_8);

        ReleaseOrigin origin = ReleaseOrigin.PUBLIC;
        String indexUrl = origin.managedPayloadChunkIndexUrl(version, "publisher");
        String zipUrl = origin.managedPayloadUrl(version, "publisher");
        String chunksBase = origin.managedChunksBaseUrl();

        JSONObject root = new JSONObject()
                .put("version", version)
                .put("gitCommit", "a".repeat(40))
                .put("downloads", new JSONObject().put("android-aarch64", new JSONObject()
                        .put("app", new JSONObject().put("url", origin.base + "/" + version + "/android-aarch64/app/a.apk")
                                .put("size", 10).put("sha256", "1".repeat(64)).put("versionCode", 6004005)
                                .put("signerSha256", "2".repeat(64)))
                        .put("loader", new JSONObject().put("tag", "v1").put("sha256", "3".repeat(64)))
                        .put("selfUpdatable", true)
                        .put("historyDatabaseUserVersion", 1)
                        .put("nonManagedPayloadDigest", "e".repeat(64))
                        .put("adaptations", new JSONObject()
                                .put("publisher", adaptationJson(origin, version, "publisher", fullZipSize, indexUrl,
                                        indexBytes, chunksBase))
                                .put("play", adaptationJson(origin, version, "play", fullZipSize, indexUrl,
                                        indexBytes, chunksBase)))));
        PlatformManifest decoded = PlatformManifest.decode(root.toString(), origin);
        return new FixtureRelease(decoded.adaptations.get("publisher").managedPayload, indexUrl, indexBytes,
                compressedChunks);
    }

    private static JSONObject adaptationJson(ReleaseOrigin origin, String version, String source, long fullZipSize,
            String publisherIndexUrl, byte[] indexBytes, String chunksBase) throws Exception {
        String idxUrl = origin.managedPayloadChunkIndexUrl(version, source);
        return new JSONObject()
                .put("signerSha256", "4".repeat(64))
                .put("game", new JSONObject().put("gameVersion", "1.0").put("versionCode", 1)
                        .put("il2cppSha256", "5".repeat(64)).put("metadataSha256", "6".repeat(64)))
                .put("interop", new JSONObject().put("url", origin.interopUrl("7".repeat(64)))
                        .put("size", 10).put("sha256", "7".repeat(64)).put("snapshotSha256", "8".repeat(64))
                        .put("signature", "sig"))
                .put("managedPayload", new JSONObject()
                        .put("url", origin.managedPayloadUrl(version, source))
                        .put("size", fullZipSize)
                        .put("sha256", "9".repeat(64))
                        .put("signature", "zip-sig")
                        .put("chunkIndex", new JSONObject()
                                .put("url", idxUrl)
                                .put("size", indexBytes.length)
                                .put("sha256", Sha256.of(indexBytes))
                                .put("signature", "idx-sig")
                                .put("chunksBaseUrl", chunksBase)));
    }

    private static final class FixtureRelease {
        final PlatformManifest.ManagedPayload managedPayload;
        final String indexUrl;
        final byte[] indexBytes;
        final Map<String, byte[]> chunks;

        FixtureRelease(PlatformManifest.ManagedPayload managedPayload, String indexUrl, byte[] indexBytes,
                Map<String, byte[]> chunks) {
            this.managedPayload = managedPayload;
            this.indexUrl = indexUrl;
            this.indexBytes = indexBytes;
            this.chunks = chunks;
        }

        Download.Source open(String url) {
            if (url.equals(indexUrl)) return () -> new ByteArrayInputStream(indexBytes);
            if (url.endsWith(".deflate")) {
                String name = url.substring(url.lastIndexOf('/') + 1);
                String sha = name.substring(0, name.length() - ".deflate".length());
                byte[] bytes = chunks.get(sha);
                if (bytes != null) return () -> new ByteArrayInputStream(bytes);
            }
            return () -> {
                throw new IOException("HTTP 404 for " + url);
            };
        }
    }

    private static byte[] deflateRaw(byte[] raw) {
        Deflater deflater = new Deflater(Deflater.DEFAULT_COMPRESSION, true);
        try {
            deflater.setInput(raw);
            deflater.finish();
            ByteArrayOutputStream out = new ByteArrayOutputStream(raw.length);
            byte[] buf = new byte[4096];
            while (!deflater.finished()) {
                int n = deflater.deflate(buf);
                out.write(buf, 0, n);
            }
            return out.toByteArray();
        } finally {
            deflater.end();
        }
    }

    private static byte[] prngBytes(int seed, int length) {
        byte[] out = new byte[length];
        int state = seed;
        for (int i = 0; i < length; i++) {
            state = state * 1664525 + 1013904223;
            out[i] = (byte) (state >>> 24);
        }
        return out;
    }

    private static byte[] hexToBytes(String hex) {
        if (hex.isEmpty()) return new byte[0];
        byte[] out = new byte[hex.length() / 2];
        for (int i = 0; i < out.length; i++) {
            out[i] = (byte) Integer.parseInt(hex.substring(i * 2, i * 2 + 2), 16);
        }
        return out;
    }
}
