package com.bazaarplusplus.android.payload;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.io.Sha256;
import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.PlatformManifest;
import com.bazaarplusplus.android.release.ReleaseOrigin;
import com.bazaarplusplus.android.release.SafeNames;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.RandomAccessFile;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import java.util.zip.DataFormatException;
import java.util.zip.Inflater;

/**
 * Stages a Managed Payload incrementally via FastCDC content-defined chunks.
 * Any failure or low-reuse threshold cleanly returns a fallback outcome so the caller
 * falls back to downloading the full Managed Payload zip without deleting installed Mods.
 */
public final class ManagedPayloadIncremental {
    private static final String TEMP_PACK_NAME = ".chunks.tmp";
    private static final int CONCURRENCY = 6;
    private static final int CHUNK_ATTEMPTS = 2;
    private static final long MAX_SEED_SCAN_BYTES = 64L * 1024 * 1024;
    private static final long PROGRESS_THROTTLE_MS = 50L;

    public interface SourceFactory {
        Download.Source open(String url, boolean identityEncoding);
    }

    public static final class Outcome {
        public final boolean staged;
        public final String fallbackReason;
        public final long downloadedBytes;
        public final List<String> removedInstalled;

        private Outcome(boolean staged, String fallbackReason, long downloadedBytes, List<String> removedInstalled) {
            this.staged = staged;
            this.fallbackReason = fallbackReason;
            this.downloadedBytes = downloadedBytes;
            this.removedInstalled = removedInstalled;
        }

        static Outcome ok(long downloadedBytes, List<String> removedInstalled) {
            return new Outcome(true, null, downloadedBytes, removedInstalled);
        }

        static Outcome fallback(String reason) {
            return new Outcome(false, reason, 0, List.of());
        }
    }

    private static final class LocalChunkSlice {
        final File file;
        final int offset;
        final int length;

        LocalChunkSlice(File file, int offset, int length) {
            this.file = file;
            this.offset = offset;
            this.length = length;
        }
    }

    private static final class PackSlice {
        final long offset;
        final int length;

        PackSlice(long offset, int length) {
            this.offset = offset;
            this.length = length;
        }
    }

    private ManagedPayloadIncremental() {}

    public static Outcome tryStage(PlatformManifest.ManagedPayload payload, String version, File externalFiles,
            File updatesDir, boolean removeInstalledOnPublish, SourceFactory sources, Download.Progress progress) {
        if (payload == null || payload.chunkIndex == null) return Outcome.fallback("no_chunk_index");
        if (!SafeNames.isFileName(version) || version.startsWith(".")) return Outcome.fallback("bad_version");

        PlatformManifest.ChunkIndex chunkIndexRef = payload.chunkIndex;
        if (chunkIndexRef.file.size <= 0 || chunkIndexRef.file.size > ManagedPayloadChunkIndex.MAX_INDEX_BYTES) {
            return Outcome.fallback("chunk_index_size_invalid");
        }

        byte[] indexBytes;
        try {
            indexBytes = fetchVerified(sources.open(chunkIndexRef.file.url, false), chunkIndexRef.file.size,
                    chunkIndexRef.file.sha256);
        } catch (IOException e) {
            return Outcome.fallback("chunk_index_fetch_failed");
        }

        ManagedPayloadChunkIndex index;
        try {
            index = ManagedPayloadChunkIndex.parse(indexBytes);
        } catch (IOException e) {
            return Outcome.fallback("chunk_index_invalid");
        }

        if (!version.equals(index.version)) return Outcome.fallback("chunk_index_version_mismatch");
        if (!ReleaseOrigin.PLATFORM_KEY.equals(index.platform)) return Outcome.fallback("chunk_index_platform_mismatch");
        for (ManagedPayloadChunkIndex.FileRecord file : index.files) {
            if (!ManagedPayloadStaging.installPath(file.path)
                    || ManagedPayloadStaging.INSTALLED_MANIFEST.equals(file.path)) {
                return Outcome.fallback("path_outside_managed_payload");
            }
        }

        Set<String> neededHashes = new HashSet<>(index.chunks.size() * 2);
        for (ManagedPayloadChunkIndex.ChunkRecord chunk : index.chunks) neededHashes.add(chunk.rawSha256);

        Map<String, LocalChunkSlice> localChunks = scanLocalSeeds(externalFiles, updatesDir, version,
                index.chunking, neededHashes);

        List<ManagedPayloadChunkIndex.ChunkRecord> missing = new ArrayList<>();
        long missingDownloadBytes = 0;
        for (ManagedPayloadChunkIndex.ChunkRecord chunk : index.chunks) {
            if (localChunks.containsKey(chunk.rawSha256)) continue;
            missing.add(chunk);
            missingDownloadBytes += chunk.compressedSize;
        }
        int localHits = index.chunks.size() - missing.size();
        if (!missing.isEmpty() && ((localHits == 0 && missing.size() > 1)
                || missingDownloadBytes * 10L >= payload.file.size * 9L)) {
            return Outcome.fallback("threshold_exceeded");
        }

        if (!updatesDir.isDirectory() && !updatesDir.mkdirs()) return Outcome.fallback("updates_dir_create_failed");
        File download = new File(updatesDir,
                ManagedPayloadStaging.DOWNLOAD_PREFIX + version + "-" + UUID.randomUUID().toString().replace("-", ""));
        File packFile = new File(download, TEMP_PACK_NAME);
        try {
            if (!download.mkdirs()) return Outcome.fallback("download_dir_create_failed");
            Map<String, PackSlice> downloadedSlices = new ConcurrentHashMap<>();
            Object packLock = new Object();

            try (RandomAccessFile pack = new RandomAccessFile(packFile, "rw")) {
                if (!missing.isEmpty()) {
                    String downloadFailure = downloadMissingChunks(chunkIndexRef.chunksBaseUrl, missing,
                            missingDownloadBytes, sources, pack, packLock, downloadedSlices, progress);
                    if (downloadFailure != null) return Outcome.fallback(downloadFailure);
                }

                String reassembleFailure = reassembleFiles(index, download, localChunks, pack, packLock,
                        downloadedSlices);
                if (reassembleFailure != null) return Outcome.fallback(reassembleFailure);
            }

            if (packFile.exists() && !packFile.delete()) return Outcome.fallback("pack_delete_failed");
            writeBytes(new File(download, ManagedPayloadStaging.MANIFEST), index.manifestBytes);
            writeBytes(new File(download, ManagedPayloadStaging.SIGNATURE), index.manifestSignatureBytes);

            List<String> removed = List.of();
            if (removeInstalledOnPublish) {
                removed = ManagedPayloadStaging.removeInstalled(externalFiles);
            }
            File staged = new File(updatesDir, version);
            if (staged.exists()) AtomicFiles.deleteTree(staged);
            if (!download.renameTo(staged)) return Outcome.fallback("publish_rename_failed");
            return Outcome.ok(indexBytes.length + missingDownloadBytes, removed);
        } catch (IOException e) {
            return Outcome.fallback("incremental_io_error");
        } finally {
            if (download.exists()) AtomicFiles.deleteTree(download);
        }
    }

    private static Map<String, LocalChunkSlice> scanLocalSeeds(File externalFiles, File updatesDir, String version,
            FastCdcChunker.Params params, Set<String> neededHashes) {
        Map<String, LocalChunkSlice> found = new HashMap<>();
        if (neededHashes.isEmpty()) return found;

        List<File> seedDirs = new ArrayList<>();
        for (String dir : ManagedPayloadStaging.INSTALL_DIRECTORIES) {
            seedDirs.add(new File(externalFiles, dir));
        }
        File existingStageFiles = new File(new File(updatesDir, version), "files");
        for (String dir : ManagedPayloadStaging.INSTALL_DIRECTORIES) {
            seedDirs.add(new File(existingStageFiles, dir));
        }

        long scannedBytes = 0;
        for (File dir : seedDirs) {
            File[] files = dir.listFiles();
            if (files == null) continue;
            Arrays.sort(files);
            for (File file : files) {
                if (found.size() == neededHashes.size()) return found;
                if (!file.isFile()) continue;
                long len = file.length();
                if (len <= 0 || len > Integer.MAX_VALUE || scannedBytes + len > MAX_SEED_SCAN_BYTES) continue;
                byte[] bytes;
                try {
                    bytes = Files.readAllBytes(file.toPath());
                } catch (IOException e) {
                    continue;
                }
                scannedBytes += bytes.length;
                int[] boundaries = FastCdcChunker.chunkBoundaries(bytes, bytes.length, params);
                int start = 0;
                for (int end : boundaries) {
                    int sliceLen = end - start;
                    String hash = Sha256.of(bytes, start, sliceLen);
                    if (neededHashes.contains(hash) && !found.containsKey(hash)) {
                        found.put(hash, new LocalChunkSlice(file, start, sliceLen));
                    }
                    start = end;
                }
            }
        }
        return found;
    }

    private static String downloadMissingChunks(String chunksBaseUrl,
            List<ManagedPayloadChunkIndex.ChunkRecord> missing, long totalDownloadBytes, SourceFactory sources,
            RandomAccessFile pack, Object packLock, Map<String, PackSlice> downloadedSlices,
            Download.Progress progress) {
        String base = chunksBaseUrl.replaceAll("/+$", "");
        ConcurrentLinkedQueue<ManagedPayloadChunkIndex.ChunkRecord> queue = new ConcurrentLinkedQueue<>(missing);
        AtomicBoolean aborted = new AtomicBoolean(false);
        AtomicReference<String> firstFailure = new AtomicReference<>(null);
        AtomicLong received = new AtomicLong(0);
        AtomicLong lastProgressMs = new AtomicLong(0);

        int workerCount = Math.min(CONCURRENCY, missing.size());
        ExecutorService pool = Executors.newFixedThreadPool(workerCount);
        List<Future<?>> futures = new ArrayList<>(workerCount);
        try {
            for (int i = 0; i < workerCount; i++) {
                futures.add(pool.submit(() -> {
                    while (!aborted.get()) {
                        ManagedPayloadChunkIndex.ChunkRecord chunk = queue.poll();
                        if (chunk == null) return;
                        String url = base + "/" + chunk.compressedSha256.substring(0, 2) + "/"
                                + chunk.compressedSha256 + ".deflate";
                        byte[] raw;
                        try {
                            raw = fetchAndInflateChunk(url, chunk, sources);
                        } catch (IOException e) {
                            firstFailure.compareAndSet(null, e.getMessage() == null ? "chunk_failed" : e.getMessage());
                            aborted.set(true);
                            return;
                        }
                        try {
                            synchronized (packLock) {
                                long offset = pack.length();
                                pack.seek(offset);
                                pack.write(raw);
                                downloadedSlices.put(chunk.rawSha256, new PackSlice(offset, raw.length));
                            }
                        } catch (IOException e) {
                            firstFailure.compareAndSet(null, "pack_write_failed");
                            aborted.set(true);
                            return;
                        }
                        long done = received.addAndGet(chunk.compressedSize);
                        if (progress != null && totalDownloadBytes > 0) {
                            long now = System.currentTimeMillis();
                            long prev = lastProgressMs.get();
                            if (done >= totalDownloadBytes
                                    || (now - prev >= PROGRESS_THROTTLE_MS && lastProgressMs.compareAndSet(prev, now))) {
                                progress.update(done, totalDownloadBytes);
                            }
                        }
                    }
                }));
            }
            for (Future<?> future : futures) {
                try {
                    future.get();
                } catch (Exception e) {
                    firstFailure.compareAndSet(null, "chunk_worker_failed");
                    aborted.set(true);
                }
            }
        } finally {
            pool.shutdownNow();
        }
        return firstFailure.get();
    }

    private static byte[] fetchAndInflateChunk(String url, ManagedPayloadChunkIndex.ChunkRecord chunk,
            SourceFactory sources) throws IOException {
        IOException lastError = null;
        for (int attempt = 1; attempt <= CHUNK_ATTEMPTS; attempt++) {
            try {
                byte[] compressed = fetchVerified(sources.open(url, true), chunk.compressedSize,
                        chunk.compressedSha256);
                byte[] raw = inflateRaw(compressed, chunk.rawSize);
                if (!Sha256.of(raw).equals(chunk.rawSha256)) {
                    throw new IOException("chunk_raw_sha256_mismatch");
                }
                return raw;
            } catch (IOException e) {
                lastError = e;
                String msg = e.getMessage();
                boolean permanent = "chunk_raw_sha256_mismatch".equals(msg)
                        || "chunk_inflate_failed".equals(msg)
                        || "sha256_mismatch".equals(msg)
                        || "size_mismatch".equals(msg);
                if (permanent || attempt >= CHUNK_ATTEMPTS) throw e;
            }
        }
        throw lastError != null ? lastError : new IOException("chunk_fetch_failed");
    }

    private static String reassembleFiles(ManagedPayloadChunkIndex index, File downloadDir,
            Map<String, LocalChunkSlice> localChunks, RandomAccessFile pack, Object packLock,
            Map<String, PackSlice> downloadedSlices) throws IOException {
        for (ManagedPayloadChunkIndex.FileRecord file : index.files) {
            File target = new File(downloadDir, ManagedPayloadStaging.FILES + file.path);
            File parent = target.getParentFile();
            if (parent != null && !parent.isDirectory() && !parent.mkdirs()) return "parent_dir_create_failed";

            Sha256 fileHash = new Sha256();
            long written = 0;
            try (OutputStream out = new FileOutputStream(target)) {
                for (int chunkIdx : file.chunks) {
                    ManagedPayloadChunkIndex.ChunkRecord record = index.chunks.get(chunkIdx);
                    byte[] raw;
                    LocalChunkSlice local = localChunks.get(record.rawSha256);
                    if (local != null) {
                        if (local.length != record.rawSize) return "local_chunk_size_mismatch";
                        try {
                            raw = readFileSlice(local.file, local.offset, local.length);
                        } catch (IOException e) {
                            return "local_chunk_read_failed";
                        }
                        if (!Sha256.of(raw).equals(record.rawSha256)) return "local_chunk_changed";
                    } else {
                        PackSlice slice = downloadedSlices.get(record.rawSha256);
                        if (slice == null) return "chunk_missing";
                        if (slice.length != record.rawSize) return "downloaded_chunk_size_mismatch";
                        raw = new byte[slice.length];
                        synchronized (packLock) {
                            pack.seek(slice.offset);
                            pack.readFully(raw);
                        }
                    }
                    out.write(raw);
                    fileHash.update(raw, 0, raw.length);
                    written += raw.length;
                }
            }
            if (written != file.size) return "file_size_mismatch";
            if (!fileHash.hex().equals(file.sha256)) return "file_hash_mismatch";
        }
        return null;
    }

    private static byte[] fetchVerified(Download.Source source, long expectedSize, String expectedSha256)
            throws IOException {
        if (expectedSize <= 0 || expectedSize > Integer.MAX_VALUE) throw new IOException("size_mismatch");
        byte[] bytes;
        try (InputStream in = source.open()) {
            bytes = readExactAtMost(in, (int) expectedSize);
        }
        if (bytes.length != expectedSize) throw new IOException("size_mismatch");
        if (!Sha256.of(bytes).equals(expectedSha256)) throw new IOException("sha256_mismatch");
        return bytes;
    }

    private static byte[] readExactAtMost(InputStream in, int limit) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream(Math.min(limit, 65536));
        byte[] buffer = new byte[1 << 14];
        int total = 0;
        int read;
        while ((read = in.read(buffer)) > 0) {
            total += read;
            if (total > limit) throw new IOException("size_mismatch");
            out.write(buffer, 0, read);
        }
        return out.toByteArray();
    }

    /** Raw deflate (RFC 1951): appends the dummy zero byte required by zlib nowrap and always ends the Inflater. */
    static byte[] inflateRaw(byte[] compressed, int expectedRawSize) throws IOException {
        byte[] padded = Arrays.copyOf(compressed, compressed.length + 1);
        Inflater inflater = new Inflater(true);
        try {
            inflater.setInput(padded, 0, padded.length);
            byte[] out = new byte[expectedRawSize];
            int total = 0;
            while (!inflater.finished()) {
                int remaining = expectedRawSize - total;
                if (remaining == 0) {
                    byte[] probe = new byte[1];
                    int extra = inflater.inflate(probe, 0, 1);
                    if (extra > 0) throw new IOException("chunk_inflate_failed");
                    if (inflater.finished()) break;
                    if (inflater.needsInput() || inflater.needsDictionary()) throw new IOException("chunk_inflate_failed");
                } else {
                    int n = inflater.inflate(out, total, remaining);
                    if (n == 0) {
                        if (inflater.finished()) break;
                        if (inflater.needsInput() || inflater.needsDictionary()) {
                            throw new IOException("chunk_inflate_failed");
                        }
                    }
                    total += n;
                }
            }
            if (total != expectedRawSize) throw new IOException("chunk_inflate_failed");
            return out;
        } catch (DataFormatException e) {
            throw new IOException("chunk_inflate_failed", e);
        } finally {
            inflater.end();
        }
    }

    private static byte[] readFileSlice(File file, int offset, int length) throws IOException {
        byte[] buf = new byte[length];
        try (RandomAccessFile raf = new RandomAccessFile(file, "r")) {
            raf.seek(offset);
            raf.readFully(buf);
        }
        return buf;
    }

    private static void writeBytes(File target, byte[] bytes) throws IOException {
        try (OutputStream out = new FileOutputStream(target)) {
            out.write(bytes);
        }
    }
}
