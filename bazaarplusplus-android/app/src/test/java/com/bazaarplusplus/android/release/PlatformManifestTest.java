package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import org.json.JSONObject;
import org.junit.Test;

import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

public class PlatformManifestTest {
    /** The shared release fixture (release/fixtures/latest/); unit tests run in app/. */
    static final File SHARED = new File("../../release/fixtures/latest/android-aarch64.json");

    // android-aarch64.json is the release tooling's; the retired A shape is the App's own resource.
    static String fixture(String name) throws IOException {
        if (name.equals(SHARED.getName())) return new String(Files.readAllBytes(SHARED.toPath()), StandardCharsets.UTF_8);
        try (InputStream in = PlatformManifestTest.class.getResourceAsStream("/release/" + name)) {
            if (in == null) throw new IOException("No test resource " + name);
            return new String(in.readAllBytes(), StandardCharsets.UTF_8);
        }
    }

    static String withRecord(String key, Object value) throws Exception {
        JSONObject root = new JSONObject(fixture("android-aarch64.json"));
        root.getJSONObject("downloads").getJSONObject("android-aarch64").put(key, value);
        return root.toString();
    }

    @Test
    public void decodesTheAndroidAppShape() throws Exception {
        JSONObject root = new JSONObject(fixture("android-aarch64.json"));
        JSONObject record = root.getJSONObject("downloads").getJSONObject("android-aarch64");
        PlatformManifest manifest = PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC);
        assertEquals(root.getString("version"), manifest.version);
        assertEquals(root.getString("gitCommit"), manifest.gitCommit);
        assertEquals(record.getJSONObject("app").getLong("versionCode"), manifest.app.versionCode);
        assertEquals(record.getJSONObject("app").getString("sha256"), manifest.app.file.sha256);
        assertEquals(record.getJSONObject("game").getLong("versionCode"), manifest.game.versionCode);
        assertEquals(record.getJSONObject("game").getString("gameVersion"), manifest.game.gameVersion);
        assertEquals(record.getJSONObject("game").getString("il2cppSha256"), manifest.game.hash.il2cppSha256);
        assertEquals(record.getJSONObject("game").getString("metadataSha256"), manifest.game.hash.metadataSha256);
        assertEquals(record.getJSONObject("interop").getString("sha256"), manifest.interop.file.sha256);
        assertEquals(record.getJSONObject("interop").getLong("size"), manifest.interop.file.size);
        assertEquals(record.getJSONObject("interop").getString("snapshotSha256"), manifest.interop.snapshotSha256);
        assertEquals(record.getJSONObject("interop").getString("signature"), manifest.interop.signature);
        assertEquals(record.getJSONObject("managedPayload").getString("sha256"), manifest.managedPayload.file.sha256);
        assertEquals(record.getJSONObject("managedPayload").getString("signature"), manifest.managedPayload.signature);
        assertEquals(record.getJSONObject("loader").getString("tag"), manifest.loader.tag);
        assertEquals(record.getBoolean("selfUpdatable"), manifest.selfUpdatable);
        assertEquals(record.getInt("historyDatabaseUserVersion"), manifest.historyDatabaseUserVersion);
        assertEquals(record.getString("nonManagedPayloadDigest"), manifest.nonManagedPayloadDigest);
    }

    @Test
    public void theInjectedApkShapeIsNoAndroidAppReleaseYet() throws Exception {
        assertNull(PlatformManifest.decode(fixture("android-aarch64.a-shape.json"), ReleaseOrigin.PUBLIC));
    }

    @Test
    public void rejectsWhatIsNotTheShape() throws Exception {
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode("[]", ReleaseOrigin.PUBLIC));
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode("{\"version\":\"6.1.0\"}",
                ReleaseOrigin.PUBLIC));
        JSONObject root = new JSONObject(fixture("android-aarch64.json"));
        root.put("version", "6.1");
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC));
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(
                withRecord("selfUpdatable", "yes"), ReleaseOrigin.PUBLIC));
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(
                withRecord("nonManagedPayloadDigest", "6".repeat(63)), ReleaseOrigin.PUBLIC));
    }

    @Test
    public void everyUrlMustLieUnderItsReleasePrefix() throws Exception {
        JSONObject root = new JSONObject(fixture("android-aarch64.json"));
        JSONObject record = root.getJSONObject("downloads").getJSONObject("android-aarch64");
        record.getJSONObject("managedPayload").put("url",
                "https://cdn.bazaarplusplus.com/installer/6.0.0/android-aarch64/managed/old.zip");
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC));

        JSONObject other = new JSONObject(fixture("android-aarch64.json"));
        other.getJSONObject("downloads").getJSONObject("android-aarch64").getJSONObject("interop").put("url",
                "https://evil.example/installer/android/interop/" + "2".repeat(64) + ".zip");
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(other.toString(), ReleaseOrigin.PUBLIC));

        JSONObject renamed = new JSONObject(fixture("android-aarch64.json"));
        renamed.getJSONObject("downloads").getJSONObject("android-aarch64").getJSONObject("interop").put("url",
                "https://cdn.bazaarplusplus.com/installer/android/interop/" + "9".repeat(64) + ".zip");
        assertThrows("the interop key is the zip's own sha256", MalformedReleaseException.class,
                () -> PlatformManifest.decode(renamed.toString(), ReleaseOrigin.PUBLIC));
    }

    @Test
    public void aDevelopmentOriginAcceptsItsOwnUrlsOnly() throws Exception {
        ReleaseOrigin dev = new ReleaseOrigin("http://127.0.0.1:8765");
        assertEquals("http://127.0.0.1:8765/latest/mod/android-aarch64.json", dev.manifestUrl());
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(fixture("android-aarch64.json"), dev));
        String local = fixture("android-aarch64.json").replace("https://cdn.bazaarplusplus.com/installer",
                "http://127.0.0.1:8765");
        assertEquals(new JSONObject(local).getString("version"), PlatformManifest.decode(local, dev).version);
        assertThrows(MalformedReleaseException.class, () -> PlatformManifest.decode(local, ReleaseOrigin.PUBLIC));
    }
}
