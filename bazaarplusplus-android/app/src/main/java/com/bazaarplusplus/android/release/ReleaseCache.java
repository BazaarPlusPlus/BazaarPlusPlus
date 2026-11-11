package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.payload.ManagedPayloadStaging;

import java.io.File;
import java.io.IOException;
import java.util.function.Function;
import java.util.function.Supplier;

/**
 * Reads the Platform Release Manifest from the network and keeps the last copy that decoded in the App's internal storage, so a launch without network uses the last
 * verified release (ADR 0012: "读不到平台清单（离线）时，用上一次校验过的清单与已下载的产物").
 */
public final class ReleaseCache {
    static final int MAX_DOCUMENT = 1 << 20;

    /** The manifest as the launcher sees it. */
    public static final class ManifestState {
        /** Null when nothing could be read. */
        public final PlatformManifest manifest;
        /** The network failed and {@link #manifest} is the cached copy. */
        public final boolean fromCache;
        /** Why the network read failed, or null. */
        public final String networkError;

        ManifestState(PlatformManifest manifest, boolean fromCache, String networkError) {
            this.manifest = manifest;
            this.fromCache = fromCache;
            this.networkError = networkError;
        }

        @Override
        public String toString() {
            if (manifest == null) return "unavailable (" + networkError + ")";
            return manifest.version + (fromCache ? " from cache (" + networkError + ")" : "");
        }
    }

    private final File dir;
    private final ReleaseOrigin origin;
    private final Function<String, Download.Source> network;
    private final Supplier<PlatformManifest> carried;

    /** {@code carried} supplies the release the APK carries, or null, for when nothing newer can be read. */
    public ReleaseCache(File dir, ReleaseOrigin origin, Function<String, Download.Source> network,
            Supplier<PlatformManifest> carried) {
        this.dir = dir;
        this.origin = origin;
        this.network = network;
        this.carried = carried;
    }

    public ManifestState manifest() {
        File cache = new File(dir, "manifest.json");
        String error;
        try {
            String text = Download.text(network.apply(origin.manifestUrl()), MAX_DOCUMENT);
            PlatformManifest manifest = PlatformManifest.decode(text, origin);
            AtomicFiles.writeUtf8(cache, text);
            return new ManifestState(manifest, false, null);
        } catch (IOException | MalformedReleaseException e) {
            error = e.getMessage();
        }
        PlatformManifest last = null;
        String cached = AtomicFiles.readUtf8(cache);
        if (cached != null) {
            try {
                last = PlatformManifest.decode(cached, origin);
            } catch (MalformedReleaseException e) {
                // A cache from another origin or an older App: as if there were none.
            }
        }
        PlatformManifest carriedRelease = carried.get();
        // An App updated offline carries a newer release than the one it last read.
        PlatformManifest best = last == null ? carriedRelease
                : carriedRelease == null || ManagedPayloadStaging.compareVersions(last.version, carriedRelease.version) >= 0
                        ? last : carriedRelease;
        return best != null ? new ManifestState(best, true, error) : new ManifestState(null, false, error);
    }
}
