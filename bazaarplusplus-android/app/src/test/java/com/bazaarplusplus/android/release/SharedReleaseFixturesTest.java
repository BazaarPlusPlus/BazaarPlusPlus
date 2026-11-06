package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.game.GameCodeHash;

import org.junit.Test;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * The App's readers against the release tooling's shared fixtures (root AGENTS.md, Contracts):
 * the interop statement bytes and the release certificate digest.
 * Unit tests run in app/.
 */
public class SharedReleaseFixturesTest {
    static final File RELEASE = new File("../../release");
    static final File INTEROP = new File(RELEASE, "fixtures/android-interop");

    /** The inputs release/android-interop.test.ts signs the shared fixtures with. */
    static final String ZIP = "1" + "0".repeat(63);
    static final GameCodeHash GAME = new GameCodeHash("2" + "0".repeat(63), "3" + "0".repeat(63));

    static byte[] bytes(File file) throws IOException {
        return Files.readAllBytes(file.toPath());
    }

    @Test
    public void theStatementIsTheReleaseToolingsBytes() throws Exception {
        assertArrayEquals(bytes(new File(INTEROP, "statement-release.txt")),
                InteropStatement.text(ZIP, GAME, InteropStatement.releasePurpose("6.1.0")));
    }

    @Test
    public void theEmbeddedCertificateIsTheOneReleaseToolingChecks() throws Exception {
        String manifest = new String(bytes(new File(RELEASE, "manifest.ts")), StandardCharsets.UTF_8);
        Matcher signer = Pattern.compile("ANDROID_RELEASE_SIGNER_SHA256 =\\s*'([0-9a-f]{64})'").matcher(manifest);
        assertTrue("release/manifest.ts names ANDROID_RELEASE_SIGNER_SHA256", signer.find());
        assertEquals(signer.group(1), InteropStatementTest.RELEASE_CERT_SHA256);
    }
}
