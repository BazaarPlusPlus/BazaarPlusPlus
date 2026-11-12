package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import org.json.JSONObject;
import org.junit.Test;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;

public class PlatformManifestTest {
    /** The shared release fixture (release/fixtures/latest/); unit tests run in app/. */
    static final File SHARED = new File("../../release/fixtures/latest/android-aarch64.json");

    static String fixture() throws IOException {
        return new String(Files.readAllBytes(SHARED.toPath()), StandardCharsets.UTF_8);
    }

    static String withRecord(String key, Object value) throws Exception {
        JSONObject root = new JSONObject(fixture());
        root.getJSONObject("downloads").getJSONObject("android-aarch64").put(key, value);
        return root.toString();
    }

    static JSONObject record(JSONObject root) throws Exception {
        return root.getJSONObject("downloads").getJSONObject("android-aarch64");
    }

    static JSONObject adaptation(JSONObject root, String source) throws Exception {
        return record(root).getJSONObject("adaptations").getJSONObject(source);
    }

    @Test
    public void decodesTheAndroidAppShape() throws Exception {
        JSONObject root = new JSONObject(fixture());
        JSONObject record = record(root);
        PlatformManifest manifest = PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC);
        assertEquals(root.getString("version"), manifest.version);
        assertEquals(root.getString("gitCommit"), manifest.gitCommit);
        assertEquals(record.getJSONObject("app").getLong("versionCode"), manifest.app.versionCode);
        assertEquals(record.getJSONObject("app").getString("sha256"), manifest.app.file.sha256);
        assertEquals(PlatformManifest.SOURCES, new ArrayList<>(manifest.adaptations.keySet()));
        for (String source : PlatformManifest.SOURCES) {
            JSONObject json = adaptation(root, source);
            PlatformManifest.Adaptation decoded = manifest.adaptations.get(source);
            assertEquals(source, decoded.source);
            assertEquals(json.getString("signerSha256"), decoded.signerSha256);
            JSONObject game = json.getJSONObject("game");
            assertEquals(game.getLong("versionCode"), decoded.game.versionCode);
            assertEquals(game.getString("gameVersion"), decoded.game.gameVersion);
            assertEquals(game.getString("il2cppSha256"), decoded.game.hash.il2cppSha256);
            assertEquals(game.getString("metadataSha256"), decoded.game.hash.metadataSha256);
            JSONObject interop = json.getJSONObject("interop");
            assertEquals(interop.getString("sha256"), decoded.interop.file.sha256);
            assertEquals(interop.getLong("size"), decoded.interop.file.size);
            assertEquals(interop.getString("snapshotSha256"), decoded.interop.snapshotSha256);
            assertEquals(interop.getString("signature"), decoded.interop.signature);
            JSONObject payload = json.getJSONObject("managedPayload");
            assertEquals(payload.getString("url"), decoded.managedPayload.file.url);
            assertEquals(payload.getString("sha256"), decoded.managedPayload.file.sha256);
            assertEquals(payload.getString("signature"), decoded.managedPayload.signature);
        }
        assertEquals(record.getJSONObject("loader").getString("tag"), manifest.loader.tag);
        assertEquals(record.getBoolean("selfUpdatable"), manifest.selfUpdatable);
        assertEquals(record.getInt("historyDatabaseUserVersion"), manifest.historyDatabaseUserVersion);
        assertEquals(record.getString("nonManagedPayloadDigest"), manifest.nonManagedPayloadDigest);
    }

    @Test
    public void rejectsWhatIsNotTheShape() throws Exception {
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode("[]", ReleaseOrigin.PUBLIC));
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode("{\"version\":\"6.1.0\"}",
                ReleaseOrigin.PUBLIC));
        JSONObject root = new JSONObject(fixture());
        root.put("version", "6.1");
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC));
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(
                withRecord("selfUpdatable", "yes"), ReleaseOrigin.PUBLIC));
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(
                withRecord("nonManagedPayloadDigest", "6".repeat(63)), ReleaseOrigin.PUBLIC));
    }

    @Test
    public void aManifestWithoutAdaptationsIsNotDecoded() throws Exception {
        // The shape an App before ADR 0019 reads: the top-level game, interop and managedPayload only.
        JSONObject root = new JSONObject(fixture());
        record(root).remove("adaptations");
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC));
    }

    @Test
    public void theTopLevelGameInteropAndManagedPayloadAreForOlderApps() throws Exception {
        JSONObject root = new JSONObject(fixture());
        record(root).remove("game");
        record(root).remove("interop");
        record(root).remove("managedPayload");
        assertEquals(2, PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC).adaptations.size());
    }

    @Test
    public void theAdaptationsAreExactlyPublisherAndPlay() throws Exception {
        JSONObject missing = new JSONObject(fixture());
        record(missing).getJSONObject("adaptations").remove("play");
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(missing.toString(), ReleaseOrigin.PUBLIC));

        JSONObject extra = new JSONObject(fixture());
        record(extra).getJSONObject("adaptations").put("amazon", adaptation(extra, "play"));
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(extra.toString(), ReleaseOrigin.PUBLIC));

        JSONObject unsigned = new JSONObject(fixture());
        adaptation(unsigned, "play").put("signerSha256", "F093884E");
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(unsigned.toString(), ReleaseOrigin.PUBLIC));
    }

    @Test
    public void eachManagedPayloadIsItsOwnSourcesKey() throws Exception {
        JSONObject swapped = new JSONObject(fixture());
        adaptation(swapped, "play").getJSONObject("managedPayload").put("url",
                adaptation(swapped, "publisher").getJSONObject("managedPayload").getString("url"));
        assertThrows("Play cannot name the publisher's Managed Payload", MalformedReleaseException.class,
                () -> PlatformManifest.decode(swapped.toString(), ReleaseOrigin.PUBLIC));

        JSONObject unsourced = new JSONObject(fixture());
        adaptation(unsourced, "publisher").getJSONObject("managedPayload").put("url",
                "https://cdn.bazaarplusplus.com/installer/3.1.2/android-aarch64/managed/BazaarPlusPlus_3.1.2_android-aarch64-managed.zip");
        assertThrows("the key before ADR 0019 names no source", MalformedReleaseException.class,
                () -> PlatformManifest.decode(unsourced.toString(), ReleaseOrigin.PUBLIC));

        JSONObject older = new JSONObject(fixture());
        adaptation(older, "play").getJSONObject("managedPayload").put("url",
                "https://cdn.bazaarplusplus.com/installer/3.1.1/android-aarch64/managed/play/BazaarPlusPlus_3.1.1_android-aarch64-play-managed.zip");
        assertThrows("another version's Managed Payload", MalformedReleaseException.class,
                () -> PlatformManifest.decode(older.toString(), ReleaseOrigin.PUBLIC));
    }

    @Test
    public void everyUrlMustLieUnderItsReleasePrefix() throws Exception {
        for (String source : PlatformManifest.SOURCES) {
            JSONObject other = new JSONObject(fixture());
            adaptation(other, source).getJSONObject("interop").put("url",
                    "https://evil.example/installer/android/interop/" + adaptation(other, source)
                            .getJSONObject("interop").getString("sha256") + ".zip");
            assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(other.toString(), ReleaseOrigin.PUBLIC));

            JSONObject renamed = new JSONObject(fixture());
            adaptation(renamed, source).getJSONObject("interop").put("url",
                    "https://cdn.bazaarplusplus.com/installer/android/interop/" + "9".repeat(64) + ".zip");
            assertThrows("the interop key is the zip's own sha256", MalformedReleaseException.class,
                    () -> PlatformManifest.decode(renamed.toString(), ReleaseOrigin.PUBLIC));
        }
    }

    @Test
    public void aDevelopmentOriginAcceptsItsOwnUrlsOnly() throws Exception {
        ReleaseOrigin dev = new ReleaseOrigin("http://127.0.0.1:8765");
        assertEquals("http://127.0.0.1:8765/latest/mod/android-aarch64.json", dev.manifestUrl());
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(fixture(), dev));
        String local = fixture().replace("https://cdn.bazaarplusplus.com/installer",
                "http://127.0.0.1:8765");
        assertEquals(new JSONObject(local).getString("version"), PlatformManifest.decode(local, dev).version);
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(local, ReleaseOrigin.PUBLIC));
    }

    @Test
    public void aCarriedReleaseHasNoApp() throws Exception {
        String carried = new String(java.nio.file.Files.readAllBytes(
                new java.io.File("../../release/fixtures/android-carried-release.json").toPath()),
                java.nio.charset.StandardCharsets.UTF_8);
        assertNull(PlatformManifest.decodeCarried(carried, ReleaseOrigin.PUBLIC).app);
        // A published manifest that lost its app is malformed, and an APK cannot name itself.
        org.junit.Assert.assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(carried, ReleaseOrigin.PUBLIC));
        org.junit.Assert.assertThrows(MalformedReleaseException.class,
                () -> PlatformManifest.decodeCarried(fixture(), ReleaseOrigin.PUBLIC));
    }
}
