package com.bazaarplusplus.android.game;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.TestZips;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.IOException;
import java.util.List;
import java.util.Map;

public class InstalledGameTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    @Test
    public void theOfficialGameCarriesNoInjectedLoader() throws IOException {
        assertFalse(InstalledGame.carriesInjectedLoader(List.of(
                TestZips.write(tmp.newFile("base.apk"), Map.of(GameCodeHash.METADATA_ENTRY, "m")).getPath(),
                TestZips.write(tmp.newFile("split.apk"), Map.of(GameCodeHash.IL2CPP_ENTRY, "i")).getPath())));
    }

    @Test
    public void anAndroidReleaseApkCarriesItsLoaderInAnyOfItsApks() throws IOException {
        assertTrue(InstalledGame.carriesInjectedLoader(List.of(
                TestZips.write(tmp.newFile("base.apk"), Map.of(GameCodeHash.IL2CPP_ENTRY, "i")).getPath(),
                TestZips.write(tmp.newFile("split.apk"),
                        Map.of("assets/LemonLoader/deployment/Mods/BazaarPlusPlus.version", "6.0.0.prod")).getPath())));
    }

    @Test
    public void baseApkComesFirstThenEverySplit() {
        assertEquals(List.of("/a/base.apk"), InstalledGame.apkPaths("/a/base.apk", null));
        assertEquals(List.of("/a/base.apk", "/a/split_config.arm64_v8a.apk"),
                InstalledGame.apkPaths("/a/base.apk", new String[] {"/a/split_config.arm64_v8a.apk"}));
    }
}
