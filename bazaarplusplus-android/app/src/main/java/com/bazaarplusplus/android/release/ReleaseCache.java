package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.payload.ManagedPayloadStaging;

import java.io.File;
import java.io.IOException;
import java.util.function.Function;
import java.util.function.Supplier;

public final class ReleaseCache {
    static final int MAX_DOCUMENT = 1 << 20;

    public static final class ManifestState {
        public final PlatformManifest manifest;
        public final boolean fromCache;
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
            }
        }
        PlatformManifest carriedRelease = carried.get();
        // An APK updated offline can carry a newer release than the cache.
        PlatformManifest best = last == null ? carriedRelease
                : carriedRelease == null || ManagedPayloadStaging.compareVersions(last.version, carriedRelease.version) >= 0
                        ? last : carriedRelease;
        return best != null ? new ManifestState(best, true, error) : new ManifestState(null, false, error);
    }
}
