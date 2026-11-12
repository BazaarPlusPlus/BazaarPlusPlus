package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.game.GameCodeHash;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The Android Platform Release Manifest {@code latest/mod/android-aarch64.json} in ADR 0019's
 * shape ("平台清单：加 adaptations 列表"): {@code downloads["android-aarch64"]} holds {@code app},
 * {@code loader}, the Managed Payload's compatibility facts and {@code adaptations}, one Game
 * Adaptation per Android source. The top-level {@code game}, {@code interop} and
 * {@code managedPayload} repeat the publisher's adaptation for Apps before ADR 0019; this App
 * reads only {@code adaptations}. release/manifest.ts {@code validateAndroidRelease} owns the shape.
 */
public final class PlatformManifest {
    /** The publisher's APK. */
    public static final String PUBLISHER = "publisher";
    /** Google Play. */
    public static final String PLAY = "play";
    /** The Android sources, in the order the manifest lists their adaptations (ADR 0019). */
    public static final List<String> SOURCES = List.of(PUBLISHER, PLAY);

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

        /** An artifact whose URL the release names exactly. */
        static Artifact exact(JSONObject json, String expected) throws MalformedReleaseException {
            String url = StrictJson.string(json, "url");
            if (!url.equals(expected)) throw new MalformedReleaseException(url + " is not " + expected);
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

    /** The game a release adapts. */
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

    /**
     * One Game Adaptation: the game of one Android source, its Interop Package and the Managed
     * Payload built against that interop. {@code signerSha256} is the source's game signing
     * certificate, by which the App tells the installed game's source.
     */
    public static final class Adaptation {
        public final String source;
        public final String signerSha256;
        public final Game game;
        public final Interop interop;
        public final ManagedPayload managedPayload;

        Adaptation(String source, String signerSha256, Game game, Interop interop, ManagedPayload managedPayload) {
            this.source = source;
            this.signerSha256 = signerSha256;
            this.game = game;
            this.interop = interop;
            this.managedPayload = managedPayload;
        }

        static Adaptation decode(JSONObject json, String source, ReleaseOrigin origin, String version)
                throws MalformedReleaseException {
            JSONObject payload = StrictJson.object(json, "managedPayload");
            return new Adaptation(source, StrictJson.sha256(json, "signerSha256"),
                    Game.decode(StrictJson.object(json, "game")), Interop.decode(StrictJson.object(json, "interop"), origin),
                    new ManagedPayload(Artifact.exact(payload, origin.managedPayloadUrl(version, source)),
                            StrictJson.string(payload, "signature")));
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
    /** Null only in the release an APK carries. */
    public final App app;
    /** One adaptation per source of {@link #SOURCES}, in that order. */
    public final Map<String, Adaptation> adaptations;
    public final Loader loader;
    public final boolean selfUpdatable;
    public final long historyDatabaseUserVersion;
    public final String nonManagedPayloadDigest;

    private PlatformManifest(String version, String gitCommit, App app, Map<String, Adaptation> adaptations,
            Loader loader, boolean selfUpdatable, long historyDatabaseUserVersion, String nonManagedPayloadDigest) {
        this.version = version;
        this.gitCommit = gitCommit;
        this.app = app;
        this.adaptations = Collections.unmodifiableMap(adaptations);
        this.loader = loader;
        this.selfUpdatable = selfUpdatable;
        this.historyDatabaseUserVersion = historyDatabaseUserVersion;
        this.nonManagedPayloadDigest = nonManagedPayloadDigest;
    }

    /** Decodes a manifest. Anything that is not ADR 0019's shape throws, a manifest without adaptations too. */
    public static PlatformManifest decode(String text, ReleaseOrigin origin) throws MalformedReleaseException {
        return decode(text, origin, false);
    }

    /** The release an APK carries: a manifest without {@code app}, which an APK cannot describe. */
    static PlatformManifest decodeCarried(String text, ReleaseOrigin origin) throws MalformedReleaseException {
        return decode(text, origin, true);
    }

    private static PlatformManifest decode(String text, ReleaseOrigin origin, boolean carried)
            throws MalformedReleaseException {
        JSONObject root;
        try {
            root = new JSONObject(text);
        } catch (JSONException e) {
            throw new MalformedReleaseException("The manifest is not a JSON object", e);
        }
        String version = StrictJson.version(root, "version");
        String gitCommit = StrictJson.string(root, "gitCommit");
        JSONObject record = StrictJson.object(StrictJson.object(root, "downloads"), ReleaseOrigin.PLATFORM_KEY);
        if (carried && record.has("app")) throw new MalformedReleaseException("A carried release names an app");

        String versioned = "/" + version + "/" + ReleaseOrigin.PLATFORM_KEY;
        App app = null;
        if (!carried) {
            JSONObject appJson = StrictJson.object(record, "app");
            app = new App(Artifact.decode(appJson, origin, versioned + "/app/"),
                    StrictJson.positive(appJson, "versionCode"), StrictJson.sha256(appJson, "signerSha256"));
        }
        long historyVersion = StrictJson.integer(record, "historyDatabaseUserVersion");
        if (historyVersion < 0) throw new MalformedReleaseException("historyDatabaseUserVersion is negative");
        return new PlatformManifest(version, gitCommit, app, adaptations(StrictJson.object(record, "adaptations"), origin,
                version), Loader.decode(StrictJson.object(record, "loader")), StrictJson.bool(record, "selfUpdatable"),
                historyVersion, StrictJson.sha256(record, "nonManagedPayloadDigest"));
    }

    private static Map<String, Adaptation> adaptations(JSONObject json, ReleaseOrigin origin, String version)
            throws MalformedReleaseException {
        // A key set, not an order: JSON objects are unordered, and a reader may not keep the order.
        if (json.length() != SOURCES.size()) throw new MalformedReleaseException("adaptations must be exactly " + SOURCES);
        Map<String, Adaptation> adaptations = new LinkedHashMap<>();
        for (String source : SOURCES) {
            adaptations.put(source, Adaptation.decode(StrictJson.object(json, source), source, origin, version));
        }
        return adaptations;
    }
}
