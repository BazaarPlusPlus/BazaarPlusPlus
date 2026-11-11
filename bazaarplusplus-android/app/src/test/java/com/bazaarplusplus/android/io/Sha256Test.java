package com.bazaarplusplus.android.io;

import static org.junit.Assert.assertEquals;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.ByteArrayInputStream;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

public class Sha256Test {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    /** FIPS 180-4 test vectors. */
    static final String EMPTY = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    static final String ABC = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    @Test
    public void theDigestIsSixtyFourLowercaseHexCharacters() throws Exception {
        assertEquals(EMPTY, Sha256.of(new ByteArrayInputStream(new byte[0])));
        assertEquals(ABC, Sha256.of(new ByteArrayInputStream("abc".getBytes(StandardCharsets.UTF_8))));
        assertEquals(EMPTY, new Sha256().hex());
    }

    @Test
    public void aStreamAFileAndUpdatesInPiecesGiveOneDigest() throws Exception {
        // Larger than one 64 KiB read buffer and not a multiple of it.
        byte[] large = new byte[3 * (1 << 16) + 123];
        for (int i = 0; i < large.length; i++) large[i] = (byte) (i * 31 + 7);
        String expected = Sha256.of(new ByteArrayInputStream(large));

        File file = tmp.newFile();
        Files.write(file.toPath(), large);
        assertEquals(expected, Sha256.of(file));

        Sha256 pieces = new Sha256();
        for (int offset = 0; offset < large.length; offset += 1000) {
            pieces.update(large, offset, Math.min(1000, large.length - offset));
        }
        assertEquals(expected, pieces.hex());
    }

    @Test
    public void aStreamIsHashedFromWhereItStands() throws Exception {
        ByteArrayInputStream in = new ByteArrayInputStream("xabc".getBytes(StandardCharsets.UTF_8));
        assertEquals('x', in.read());
        assertEquals(ABC, Sha256.of(in));
    }
}
