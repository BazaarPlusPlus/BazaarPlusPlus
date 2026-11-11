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
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.Locale;
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
    public void theDownloadDigestIsGameCodeHashSha256InLowercaseHex() throws Exception {
        assertEquals("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", sha(new byte[0]));
        // Larger than one 64 KiB read buffer and not a multiple of it.
        byte[] large = new byte[3 * (1 << 16) + 123];
        for (int i = 0; i < large.length; i++) large[i] = (byte) (i * 31 + 7);
        for (byte[] bytes : new byte[][] {new byte[0], large}) {
            String expected = sha(bytes);
            File file = tmp.newFile();
            Files.write(file.toPath(), bytes);
            try (InputStream in = new FileInputStream(file)) {
                assertEquals(expected, GameCodeHash.Sha256.of(in));
            }
            assertTrue(bytes.length + " bytes", Download.matches(file, bytes.length, expected));
            assertFalse("uppercase hex is another digest",
                    Download.matches(file, bytes.length, expected.toUpperCase(Locale.ROOT)));

            File fetched = new File(tmp.getRoot(), "fetched-" + bytes.length + ".zip");
            Download.fetch(() -> new ByteArrayInputStream(bytes), bytes.length, expected, fetched, null);
            assertArrayEquals(bytes, Files.readAllBytes(fetched.toPath()));
        }
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
