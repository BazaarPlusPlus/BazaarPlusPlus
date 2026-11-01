package com.bazaarplusplus.android.game;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.TestZips;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.IOException;
import java.util.List;
import java.util.Map;

public class GameCodeHashTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();


    @Test
    public void hashesBothEntriesOfOneApk() throws IOException {
        String apk = TestZips.write(tmp.newFile("base.apk"), Map.of(
                GameCodeHash.IL2CPP_ENTRY, "il2cpp",
                GameCodeHash.METADATA_ENTRY, "metadata",
                "lib/arm64-v8a/libunity.so", "unity")).getPath();
        GameCodeHash hash = GameCodeHash.compute(List.of(apk));
        assertEquals(sha256("il2cpp"), hash.il2cppSha256);
        assertEquals(sha256("metadata"), hash.metadataSha256);
        assertTrue(hash.matches(sha256("il2cpp"), sha256("metadata")));
    }

    @Test
    public void findsEachEntryInWhicheverSplitHoldsIt() throws IOException {
        String base = TestZips.write(tmp.newFile("base.apk"), Map.of(GameCodeHash.METADATA_ENTRY, "metadata")).getPath();
        String split = TestZips.write(tmp.newFile("split_config.arm64_v8a.apk"),
                Map.of(GameCodeHash.IL2CPP_ENTRY, "il2cpp")).getPath();
        assertEquals(new GameCodeHash(sha256("il2cpp"), sha256("metadata")), GameCodeHash.compute(List.of(base, split)));
    }

    @Test
    public void refusesAMissingOrDuplicatedEntry() throws IOException {
        String onlyMetadata = TestZips.write(tmp.newFile("a.apk"), Map.of(GameCodeHash.METADATA_ENTRY, "m")).getPath();
        assertThrows(IOException.class, () -> GameCodeHash.compute(List.of(onlyMetadata)));
        String both = TestZips.write(tmp.newFile("b.apk"), Map.of(
                GameCodeHash.IL2CPP_ENTRY, "i", GameCodeHash.METADATA_ENTRY, "m")).getPath();
        String again = TestZips.write(tmp.newFile("c.apk"), Map.of(GameCodeHash.IL2CPP_ENTRY, "i")).getPath();
        assertThrows(IOException.class, () -> GameCodeHash.compute(List.of(both, again)));
    }

    static String sha256(String text) throws IOException {
        return GameCodeHash.Sha256.of(new java.io.ByteArrayInputStream(text.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
    }
}
