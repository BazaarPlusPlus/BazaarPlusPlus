package com.bazaarplusplus.android.game;

import static org.junit.Assert.assertEquals;

import com.bazaarplusplus.android.TestZips;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;
import java.io.IOException;
import java.util.List;
import java.util.Map;

public class LibraryLinksTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    @Test
    public void theAppsLibrariesReplaceTheGamesAndUnextractedOnesGetNoLink() throws IOException {
        File gameLibs = tmp.newFolder("game-lib");
        new File(gameLibs, "libunity.so").createNewFile();
        new File(gameLibs, "libmain.so").createNewFile();
        File appLibs = tmp.newFolder("app-lib");
        Map<String, File> plan = LibraryLinks.plan(List.of("libunity.so", "libmain.so", "libil2cpp.so"), gameLibs,
                List.of("libmain.so", "libe_sqlite3.so"), appLibs);
        assertEquals(Map.of(
                "libunity.so", new File(gameLibs, "libunity.so"),
                "libmain.so", new File(appLibs, "libmain.so"),
                "libe_sqlite3.so", new File(appLibs, "libe_sqlite3.so")), plan);
    }

    @Test
    public void listsOnlyTopLevelArm64LibrariesOfEveryApk() throws IOException {
        String base = TestZips.write(tmp.newFile("base.apk"), Map.of(
                "lib/arm64-v8a/libunity.so", "", "lib/armeabi-v7a/libunity.so", "", "assets/x.so", "")).getPath();
        String split = TestZips.write(tmp.newFile("split.apk"), Map.of(
                "lib/arm64-v8a/libil2cpp.so", "", "lib/arm64-v8a/sub/libnested.so", "")).getPath();
        assertEquals(List.of("libunity.so", "libil2cpp.so"), LibraryLinks.libraryNames(List.of(base, split)));
    }

    @Test
    public void theSearchPathEndsWithEachApksLibraryDirectory() {
        assertEquals("/l:/app:/game:/a/base.apk!/lib/arm64-v8a:/a/split.apk!/lib/arm64-v8a",
                LibraryLinks.librarySearchPath(new File("/l"), new File("/app"), new File("/game"),
                        List.of("/a/base.apk", "/a/split.apk")));
    }
}
