package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.game.GameCodeHash;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * The Android Platform Release Manifest {@code latest/mod/android-aarch64.json} in ADR 0012's
 * shape ("fragment 与平台清单"): {@code downloads["android-aarch64"]} holds {@code app},
 * {@code game}, {@code interop}, {@code managedPayload}, {@code loader} and the Managed Payload's
 * compatibility facts. release/downloads.ts {@code decodeAndroidDownload} reads the same shape for
 * the site and the mod.
 */
public final class PlatformManifest {
    /** A downloadable file the manifest pins by size and sha256. */
    public static final class Artifact {
        public final String url;
        public final long size;
        public final String sha256;

        Artifact(String url, long size, String sha256) {
            this.url = url;
            this.size = size;
            this.sha256 = sha256;
        }

        static Artifact decode(JSONObject json, ReleaseOrigin origin, String prefix) throws MalformedReleaseException {
            String url = StrictJson.string(json, "url");
            if (!origin.under(url, prefix)) throw new MalformedReleaseException(url + " is not under " + origin.base + prefix);
            return new Artifact(url, StrictJson.positive(json, "size"), StrictJson.sha256(json, "sha256"));
        }
    }

    public static final class App {
        public final Artifact file;
        public final long versionCode;
        public final String signerSha256;

        App(Artifact file, long versionCode, String signerSha256) {
            this.file = file;
            this.versionCode = versionCode;
            this.signerSha256 = signerSha256;
        }
    }

    /** The game a release (or a Pre-Adaptation) adapts. */
    public static final class Game {
        public final String gameVersion;
        public final long versionCode;
        public final GameCodeHash hash;

        Game(String gameVersion, long versionCode, GameCodeHash hash) {
            this.gameVersion = gameVersion;
            this.versionCode = versionCode;
            this.hash = hash;
        }

        static Game decode(JSONObject json) throws MalformedReleaseException {
            return new Game(StrictJson.string(json, "gameVersion"), StrictJson.positive(json, "versionCode"),
                    new GameCodeHash(StrictJson.sha256(json, "il2cppSha256"), StrictJson.sha256(json, "metadataSha256")));
        }
    }

    /** An Interop Package: the zip, its Interop Snapshot digest and its signed interop statement. */
    public static final class Interop {
        public final Artifact file;
        public final String snapshotSha256;
        public final String signature;

        Interop(Artifact file, String snapshotSha256, String signature) {
            this.file = file;
            this.snapshotSha256 = snapshotSha256;
            this.signature = signature;
        }

        static Interop decode(JSONObject json, ReleaseOrigin origin) throws MalformedReleaseException {
            Artifact file = Artifact.decode(json, origin, "/android/interop/");
            if (!file.url.equals(origin.interopUrl(file.sha256))) {
                throw new MalformedReleaseException(file.url + " is not the content-addressed key of " + file.sha256);
            }
            return new Interop(file, StrictJson.sha256(json, "snapshotSha256"), StrictJson.string(json, "signature"));
        }
    }

    public static final class ManagedPayload {
        public final Artifact file;
        public final String signature;

        ManagedPayload(Artifact file, String signature) {
            this.file = file;
            this.signature = signature;
        }
    }

    public static final class Loader {
        public final String tag;
        public final String sha256;

        Loader(String tag, String sha256) {
            this.tag = tag;
            this.sha256 = sha256;
        }

        static Loader decode(JSONObject json) throws MalformedReleaseException {
            String tag = StrictJson.string(json, "tag");
            if (!ReleaseOrigin.LOADER_TAG.matcher(tag).matches()) throw new MalformedReleaseException("Bad loader tag " + tag);
            return new Loader(tag, StrictJson.sha256(json, "sha256"));
        }
    }

    public final String version;
    public final String gitCommit;
    public final App app;
    public final Game game;
    public final Interop interop;
    public final ManagedPayload managedPayload;
    public final Loader loader;
    public final boolean selfUpdatable;
    public final long historyDatabaseUserVersion;
    public final String nonManagedPayloadDigest;

    private PlatformManifest(String version, String gitCommit, App app, Game game, Interop interop,
            ManagedPayload managedPayload, Loader loader, boolean selfUpdatable, long historyDatabaseUserVersion,
            String nonManagedPayloadDigest) {
        this.version = version;
        this.gitCommit = gitCommit;
        this.app = app;
        this.game = game;
        this.interop = interop;
        this.managedPayload = managedPayload;
        this.loader = loader;
        this.selfUpdatable = selfUpdatable;
        this.historyDatabaseUserVersion = historyDatabaseUserVersion;
        this.nonManagedPayloadDigest = nonManagedPayloadDigest;
    }

    /**
     * Decodes a manifest. Returns null for the retired injected-APK shape ({@code apk} without
     * {@code app}): that is "no Android App release yet", not an error. Anything else that is not
     * ADR 0012's shape throws.
     */
    public static PlatformManifest decode(String text, ReleaseOrigin origin) throws MalformedReleaseException {
        JSONObject root;
        try {
            root = new JSONObject(text);
        } catch (JSONException e) {
            throw new MalformedReleaseException("The manifest is not a JSON object", e);
        }
        String version = StrictJson.version(root, "version");
        String gitCommit = StrictJson.string(root, "gitCommit");
        JSONObject record = StrictJson.object(StrictJson.object(root, "downloads"), ReleaseOrigin.PLATFORM_KEY);
        if (!record.has("app") && record.has("apk")) return null;

        JSONObject appJson = StrictJson.object(record, "app");
        String versioned = "/" + version + "/" + ReleaseOrigin.PLATFORM_KEY;
        App app = new App(Artifact.decode(appJson, origin, versioned + "/app/"),
                StrictJson.positive(appJson, "versionCode"), StrictJson.sha256(appJson, "signerSha256"));
        JSONObject payloadJson = StrictJson.object(record, "managedPayload");
        ManagedPayload payload = new ManagedPayload(Artifact.decode(payloadJson, origin, versioned + "/managed/"),
                StrictJson.string(payloadJson, "signature"));
        long historyVersion = StrictJson.integer(record, "historyDatabaseUserVersion");
        if (historyVersion < 0) throw new MalformedReleaseException("historyDatabaseUserVersion is negative");
        return new PlatformManifest(version, gitCommit, app, Game.decode(StrictJson.object(record, "game")),
                Interop.decode(StrictJson.object(record, "interop"), origin), payload,
                Loader.decode(StrictJson.object(record, "loader")), StrictJson.bool(record, "selfUpdatable"),
                historyVersion, StrictJson.sha256(record, "nonManagedPayloadDigest"));
    }
}
