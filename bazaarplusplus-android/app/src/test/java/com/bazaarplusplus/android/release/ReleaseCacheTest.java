package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONObject;
import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;
import java.util.function.Function;

public class ReleaseCacheTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    final Map<String, String> served = new HashMap<>();
    final Function<String, Download.Source> network = url -> () -> {
        String body = served.get(url);
        if (body == null) throw new IOException("HTTP 404 for " + url);
        return new ByteArrayInputStream(body.getBytes(StandardCharsets.UTF_8));
    };

    PlatformManifest carried;

    ReleaseCache cache() {
        return new ReleaseCache(tmp.getRoot(), ReleaseOrigin.PUBLIC, network, () -> carried);
    }

    static PlatformManifest carriedFixture() throws Exception {
        return PlatformManifest.decodeCarried(new String(java.nio.file.Files.readAllBytes(
                new java.io.File("../../release/fixtures/android-carried-release.json").toPath()), StandardCharsets.UTF_8),
                ReleaseOrigin.PUBLIC);
    }

    @Test
    public void aNewInstallWithNoNetworkUsesTheCarriedRelease() throws Exception {
        carried = carriedFixture();
        ReleaseCache.ManifestState state = cache().manifest();
        assertEquals("3.1.2", state.manifest.version);
        assertNull("a carried release cannot name its own APK", state.manifest.app);
        assertTrue(state.fromCache);
    }

    @Test
    public void theNewerOfTheCacheAndTheCarriedReleaseWinsOffline() throws Exception {
        String fixture = PlatformManifestTest.fixture();
        served.put(ReleaseOrigin.PUBLIC.manifestUrl(), fixture);
        cache().manifest();
        served.clear();
        carried = carriedFixture();
        assertNotNull("the same version keeps the read manifest, which names the APK", cache().manifest().manifest.app);
    }

    @Test
    public void aFetchedManifestWinsOverTheCarriedRelease() throws Exception {
        carried = carriedFixture();
        served.put(ReleaseOrigin.PUBLIC.manifestUrl(), PlatformManifestTest.fixture());
        ReleaseCache.ManifestState state = cache().manifest();
        assertFalse(state.fromCache);
        assertNotNull(state.manifest.app);
    }

    @Test
    public void aDecodedManifestIsCachedForOfflineUse() throws Exception {
        String fixture = PlatformManifestTest.fixture();
        String version = new JSONObject(fixture).getString("version");
        served.put(ReleaseOrigin.PUBLIC.manifestUrl(), fixture);
        ReleaseCache.ManifestState online = cache().manifest();
        assertEquals(version, online.manifest.version);
        assertFalse(online.fromCache);

        served.clear();
        ReleaseCache.ManifestState offline = cache().manifest();
        assertEquals(version, offline.manifest.version);
        assertTrue(offline.fromCache);

        served.put(ReleaseOrigin.PUBLIC.manifestUrl(), "{\"version\": \"broken\"}");
        ReleaseCache.ManifestState malformed = cache().manifest();
        assertTrue("a malformed manifest does not replace the last verified one", malformed.fromCache);
        assertEquals(version, malformed.manifest.version);
    }

    @Test
    public void aCacheWithoutAdaptationsGivesWayToTheCarriedRelease() throws Exception {
        // What an App before ADR 0019 cached: decoding it fails, as if there were no cache.
        JSONObject older = new JSONObject(PlatformManifestTest.fixture());
        PlatformManifestTest.record(older).remove("adaptations");
        com.bazaarplusplus.android.io.AtomicFiles.writeUtf8(new java.io.File(tmp.getRoot(), "manifest.json"),
                older.toString());
        assertNull("no carried release, nothing", cache().manifest().manifest);

        carried = carriedFixture();
        ReleaseCache.ManifestState state = cache().manifest();
        assertTrue(state.fromCache);
        assertNull("the carried release, which names no APK", state.manifest.app);
        assertEquals(2, state.manifest.adaptations.size());
    }

    @Test
    public void noNetworkAndNoCacheIsUnavailable() {
        ReleaseCache.ManifestState state = cache().manifest();
        assertNull(state.manifest);
    }
}
