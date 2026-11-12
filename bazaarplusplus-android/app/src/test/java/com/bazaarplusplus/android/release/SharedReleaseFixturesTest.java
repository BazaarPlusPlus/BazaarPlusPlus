package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.TestZips;
import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.payload.ManagedPayloadStaging;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * The App's readers against the release tooling's shared fixtures (root AGENTS.md, Contracts):
 * the interop statement bytes, the release certificate digest and the Managed Payload Manifest.
 * Unit tests run in app/.
 */
public class SharedReleaseFixturesTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

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

    /**
     * The App reads one line of a Managed Payload Manifest: its version. The golden is the one
     * release/managed-payload.test.ts writes, for version 6.1.0.
     */
    @Test
    public void theManagedPayloadManifestGoldenNamesTheVersionTheAppStagesUnder() throws Exception {
        String golden = new String(bytes(new File(RELEASE, "fixtures/managed-payload/manifest.txt")),
                StandardCharsets.UTF_8);
        assertEquals("6.1.0", ManagedPayloadStaging.manifestVersion(golden));

        Map<String, String> entries = new LinkedHashMap<>();
        entries.put(ManagedPayloadStaging.MANIFEST, golden);
        entries.put(ManagedPayloadStaging.SIGNATURE, "untrusted comment: x\nRW...\n");
        File updates = new File(tmp.getRoot(), "Updates");
        File staged = ManagedPayloadStaging.stage(TestZips.write(tmp.newFile("p.zip"), entries), updates, "6.1.0");
        assertArrayEquals(golden.getBytes(StandardCharsets.UTF_8), bytes(new File(staged, ManagedPayloadStaging.MANIFEST)));
    }

    /**
     * On a game switch the App reads the golden's {@code file} lines as well: it deletes each path
     * in Mods/ or UserLibs/, and refuses the desktop paths the golden names.
     */
    @Test
    public void theManagedPayloadManifestGoldenNamesTheFilesTheAppRemoves() throws Exception {
        String golden = new String(bytes(new File(RELEASE, "fixtures/managed-payload/manifest.txt")),
                StandardCharsets.UTF_8);
        File desktop = installed(golden);
        IOException refused = assertThrows(IOException.class, () -> ManagedPayloadStaging.removeInstalled(desktop));
        assertTrue(refused.getMessage(), refused.getMessage().contains(" BepInEx/plugins/"));
        assertTrue(new File(desktop, ManagedPayloadStaging.INSTALLED_MANIFEST).isFile());

        File android = installed(golden.replace(" BepInEx/plugins/", " MelonLoader/Mods/"));
        assertEquals(List.of("MelonLoader/Mods/BazaarPlusPlus.Core.dll", "MelonLoader/Mods/BazaarPlusPlus.dll"),
                ManagedPayloadStaging.removeInstalled(android));
        assertFalse(new File(android, ManagedPayloadStaging.INSTALLED_MANIFEST).exists());
    }

    File installed(String manifest) throws IOException {
        File external = tmp.newFolder();
        File file = new File(external, ManagedPayloadStaging.INSTALLED_MANIFEST);
        assertTrue(file.getParentFile().mkdirs());
        Files.write(file.toPath(), manifest.getBytes(StandardCharsets.UTF_8));
        return external;
    }
}
