package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.game.GameCodeHash;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.ByteArrayInputStream;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.concurrent.atomic.AtomicInteger;

public class DownloadTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static final byte[] BODY = "interop bytes".getBytes(StandardCharsets.UTF_8);

    static String sha(byte[] bytes) throws Exception {
        return GameCodeHash.Sha256.of(new ByteArrayInputStream(bytes));
    }

    @Test
    public void aVerifiedDownloadIsRenamedIntoPlaceReadOnly() throws Exception {
        File target = new File(tmp.getRoot(), "cache/x.zip");
        Download.fetch(() -> new ByteArrayInputStream(BODY), BODY.length, sha(BODY), target, null);
        assertArrayEquals(BODY, Files.readAllBytes(target.toPath()));
        assertFalse(target.canWrite());
        assertFalse(new File(target.getPath() + ".download").exists());
    }

    @Test
    public void aWrongSizeOrHashLeavesNothingBehind() throws Exception {
        File target = new File(tmp.getRoot(), "x.zip");
        assertThrows(java.io.IOException.class, () -> Download.fetch(() -> new ByteArrayInputStream(BODY),
                BODY.length, "0".repeat(64), target, null));
        assertThrows(java.io.IOException.class, () -> Download.fetch(() -> new ByteArrayInputStream(BODY),
                BODY.length - 1, sha(BODY), target, null));
        assertThrows(java.io.IOException.class, () -> Download.fetch(() -> new ByteArrayInputStream(BODY),
                BODY.length + 1, sha(BODY), target, null));
        assertFalse(target.exists());
        assertFalse(new File(target.getPath() + ".download").exists());
    }

    @Test
    public void aMatchingCacheIsReusedAndACorruptOneIsReplaced() throws Exception {
        File target = new File(tmp.getRoot(), "x.zip");
        AtomicInteger opened = new AtomicInteger();
        Download.Source source = () -> {
            opened.incrementAndGet();
            return new ByteArrayInputStream(BODY);
        };
        Download.cachedOrFetch(source, BODY.length, sha(BODY), target, null);
        Download.cachedOrFetch(source, BODY.length, sha(BODY), target, null);
        assertEquals(1, opened.get());

        target.setWritable(true);
        Files.write(target.toPath(), "interop bytez".getBytes(StandardCharsets.UTF_8));
        Download.cachedOrFetch(source, BODY.length, sha(BODY), target, null);
        assertEquals(2, opened.get());
        assertTrue(Download.matches(target, BODY.length, sha(BODY)));
    }
}
