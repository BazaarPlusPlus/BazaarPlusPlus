package com.bazaarplusplus.android.game;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

import java.util.List;

public class InstalledGameTest {
    @Test
    public void baseApkComesFirstThenEverySplit() {
        assertEquals(List.of("/a/base.apk"), InstalledGame.apkPaths("/a/base.apk", null));
        assertEquals(List.of("/a/base.apk", "/a/split_config.arm64_v8a.apk"),
                InstalledGame.apkPaths("/a/base.apk", new String[] {"/a/split_config.arm64_v8a.apk"}));
    }
}
