package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.game.GameCodeHash;

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

    ReleaseCache cache() {
        return new ReleaseCache(tmp.getRoot(), ReleaseOrigin.PUBLIC, network);
    }

    @Test
    public void aDecodedManifestIsCachedForOfflineUse() throws Exception {
        String fixture = PlatformManifestTest.fixture("android-aarch64.json");
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
    public void noNetworkAndNoCacheIsUnavailable() {
        ReleaseCache.ManifestState state = cache().manifest();
        assertNull(state.manifest);
        assertFalse(state.noRelease);
    }

    @Test
    public void theInjectedApkShapeIsNoReleaseAndIsNotCached() throws Exception {
        served.put(ReleaseOrigin.PUBLIC.manifestUrl(), PlatformManifestTest.fixture("android-aarch64.a-shape.json"));
        ReleaseCache.ManifestState state = cache().manifest();
        assertTrue(state.noRelease);
        assertNull(state.manifest);
        served.clear();
        assertFalse(cache().manifest().noRelease);
    }

    @Test
    public void aPreAdaptationIsKeptForOfflineUse() throws Exception {
        GameCodeHash game = PreAdaptationTest.GAME;
        String url = ReleaseOrigin.PUBLIC.preAdaptationUrl(game, PreAdaptationTest.TAG);
        assertNull(cache().preAdaptation(game, PreAdaptationTest.TAG));
        served.put(url, PreAdaptationTest.record(game, PreAdaptationTest.TAG, "c2ln"));
        assertEquals(12900, cache().preAdaptation(game, PreAdaptationTest.TAG).game.versionCode);
        served.clear();
        assertEquals(12900, cache().preAdaptation(game, PreAdaptationTest.TAG).game.versionCode);
    }
}
