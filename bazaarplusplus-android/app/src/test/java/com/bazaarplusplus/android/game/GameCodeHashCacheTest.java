package com.bazaarplusplus.android.game;

import static org.junit.Assert.assertEquals;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;
import java.nio.file.Files;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

public class GameCodeHashCacheTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static final GameCodeHash HASH = new GameCodeHash("a".repeat(64), "b".repeat(64));
    static final List<String> APKS = List.of("/data/app/x/base.apk", "/data/app/x/split_config.arm64_v8a.apk");

    @Test
    public void reusesTheHashWhileTheInstallIsUnchanged() throws Exception {
        File cache = new File(tmp.getRoot(), "game-code-hash.json");
        AtomicInteger computed = new AtomicInteger();
        GameCodeHashCache.Computer computer = apks -> {
            computed.incrementAndGet();
            return HASH;
        };
        assertEquals(HASH, GameCodeHashCache.get(cache, 100, APKS, computer));
        assertEquals(HASH, GameCodeHashCache.get(cache, 100, APKS, computer));
        assertEquals(1, computed.get());

        GameCodeHashCache.get(cache, 101, APKS, computer);
        assertEquals("a new lastUpdateTime", 2, computed.get());
        GameCodeHashCache.get(cache, 101, List.of("/data/app/y/base.apk"), computer);
        assertEquals("other APK paths", 3, computed.get());
    }

    @Test
    public void anUnreadableCacheIsComputedAgain() throws Exception {
        File cache = tmp.newFile("game-code-hash.json");
        Files.write(cache.toPath(), "{".getBytes());
        AtomicInteger computed = new AtomicInteger();
        GameCodeHashCache.get(cache, 100, APKS, apks -> {
            computed.incrementAndGet();
            return HASH;
        });
        assertEquals(1, computed.get());
    }
}
