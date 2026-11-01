package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.io.AtomicFiles;

import java.io.File;
import java.io.IOException;
import java.util.function.Function;

/**
 * Reads the Platform Release Manifest and Pre-Adaptation records from the network and keeps the
 * last copy that decoded in the App's internal storage, so a launch without network uses the last
 * verified release (ADR 0012: "读不到平台清单（离线）时，用上一次校验过的清单与已下载的产物").
 */
public final class ReleaseCache {
    static final int MAX_DOCUMENT = 1 << 20;

    /** The manifest as the launcher sees it. */
    public static final class ManifestState {
        /** Null when there is no release (see {@link #noRelease}) or nothing could be read. */
        public final PlatformManifest manifest;
        /** The manifest is the retired injected-APK shape: no Android App release yet. */
        public final boolean noRelease;
        /** The network failed and {@link #manifest} is the cached copy. */
        public final boolean fromCache;
        /** Why the network read failed, or null. */
        public final String networkError;

        ManifestState(PlatformManifest manifest, boolean noRelease, boolean fromCache, String networkError) {
            this.manifest = manifest;
            this.noRelease = noRelease;
            this.fromCache = fromCache;
            this.networkError = networkError;
        }

        @Override
        public String toString() {
            if (noRelease) return "no Android App release";
            if (manifest == null) return "unavailable (" + networkError + ")";
            return manifest.version + (fromCache ? " from cache (" + networkError + ")" : "");
        }
    }

    private final File dir;
    private final ReleaseOrigin origin;
    private final Function<String, Download.Source> network;

    public ReleaseCache(File dir, ReleaseOrigin origin, Function<String, Download.Source> network) {
        this.dir = dir;
        this.origin = origin;
        this.network = network;
    }

    public ManifestState manifest() {
        File cache = new File(dir, "manifest.json");
        String error;
        try {
            String text = Download.text(network.apply(origin.manifestUrl()), MAX_DOCUMENT);
            PlatformManifest manifest = PlatformManifest.decode(text, origin);
            if (manifest == null) return new ManifestState(null, true, false, null);
            AtomicFiles.writeUtf8(cache, text);
            return new ManifestState(manifest, false, false, null);
        } catch (IOException | MalformedReleaseException e) {
            error = e.getMessage();
        }
        String cached = AtomicFiles.readUtf8(cache);
        if (cached != null) {
            try {
                PlatformManifest manifest = PlatformManifest.decode(cached, origin);
                if (manifest != null) return new ManifestState(manifest, false, true, error);
            } catch (MalformedReleaseException e) {
                // A cache from another origin or an older App: as if there were none.
            }
        }
        return new ManifestState(null, false, false, error);
    }

    /**
     * The Pre-Adaptation record for this game code and Loader Release, from the network or the
     * last copy that decoded; null when there is none. Whether it is usable is the caller's check.
     */
    public PreAdaptation preAdaptation(GameCodeHash game, String loaderTag) {
        File cache = new File(dir, "adaptations/" + game + "_" + loaderTag + ".json");
        try {
            String text = Download.text(network.apply(origin.preAdaptationUrl(game, loaderTag)), MAX_DOCUMENT);
            PreAdaptation record = PreAdaptation.decode(text, origin);
            AtomicFiles.writeUtf8(cache, text);
            return record;
        } catch (IOException | MalformedReleaseException | IllegalArgumentException e) {
            // Not published yet, offline, or malformed: fall back to the last copy.
        }
        String cached = AtomicFiles.readUtf8(cache);
        if (cached == null) return null;
        try {
            return PreAdaptation.decode(cached, origin);
        } catch (MalformedReleaseException e) {
            return null;
        }
    }
}
