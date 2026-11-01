package com.bazaarplusplus.android.payload;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.TestZips;

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

public class ManagedPayloadStagingTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static final String MANIFEST = "bpp-managed-payload-manifest 1\nversion 6.1.0\nplatform android-aarch64\n";

    Map<String, String> payload() {
        Map<String, String> entries = new LinkedHashMap<>();
        entries.put("manifest.txt", MANIFEST);
        entries.put("manifest.txt.minisig", "untrusted comment: x\nRW...\n");
        entries.put("files/MelonLoader/Mods/BazaarPlusPlus.Core.dll", "core");
        entries.put("files/MelonLoader/UserLibs/BazaarPlusPlus.Storage.dll", "storage");
        return entries;
    }

    File updates() {
        return new File(tmp.getRoot(), "BazaarPlusPlusV6/Updates");
    }

    void assertNothingStaged() {
        String[] left = updates().list();
        assertTrue("left " + (left == null ? "" : String.join(",", left)), left == null || left.length == 0);
    }

    @Test
    public void stagesTheAllowedEntriesUnderTheVersion() throws Exception {
        File staged = ManagedPayloadStaging.stage(TestZips.write(tmp.newFile("p.zip"), payload()), updates(), "6.1.0");
        assertEquals(new File(updates(), "6.1.0"), staged);
        assertEquals(MANIFEST, new String(Files.readAllBytes(new File(staged, "manifest.txt").toPath()),
                StandardCharsets.UTF_8));
        assertTrue(new File(staged, "manifest.txt.minisig").isFile());
        assertTrue(new File(staged, "files/MelonLoader/Mods/BazaarPlusPlus.Core.dll").isFile());
        assertTrue(new File(staged, "files/MelonLoader/UserLibs/BazaarPlusPlus.Storage.dll").isFile());
        assertEquals("no download directory remains", 1, updates().list().length);
    }

    @Test
    public void aZipSlipEntryRejectsTheWholeZip() throws Exception {
        for (String evil : List.of("files/MelonLoader/Mods/../../../../MelonLoader/Plugins/x.dll",
                "files/MelonLoader/Mods/..", "files/MelonLoader/Mods/a\\b.dll", "../manifest.txt",
                "/manifest.txt")) {
            Map<String, String> entries = payload();
            entries.put(evil, "x");
            File zip = TestZips.write(tmp.newFile(), entries);
            assertThrows(evil, IOException.class, () -> ManagedPayloadStaging.stage(zip, updates(), "6.1.0"));
            assertNothingStaged();
        }
    }

    @Test
    public void aStrayEntryRejectsTheWholeZip() throws Exception {
        for (String stray : List.of("files/MelonLoader/Plugins/BazaarPlusPlus.Updater.dll",
                "files/MelonLoader/Mods/sub/x.dll", "files/BazaarPlusPlusV6/history.db", "README.txt",
                "files/MelonLoader/Mods/")) {
            Map<String, String> entries = payload();
            entries.put(stray, "x");
            File zip = TestZips.write(tmp.newFile(), entries);
            assertThrows(stray, IOException.class, () -> ManagedPayloadStaging.stage(zip, updates(), "6.1.0"));
            assertNothingStaged();
        }
    }

    @Test
    public void aMissingSignatureOrAnotherVersionIsRejected() throws Exception {
        Map<String, String> unsigned = payload();
        unsigned.remove("manifest.txt.minisig");
        File zip = TestZips.write(tmp.newFile(), unsigned);
        assertThrows(IOException.class, () -> ManagedPayloadStaging.stage(zip, updates(), "6.1.0"));
        File other = TestZips.write(tmp.newFile(), payload());
        assertThrows(IOException.class, () -> ManagedPayloadStaging.stage(other, updates(), "6.2.0"));
        assertNothingStaged();
    }

    @Test
    public void pruneKeepsOnlyThisLaunchsVersion() throws Exception {
        assertTrue(new File(updates(), "6.0.0/files").mkdirs());
        assertTrue(new File(updates(), ".download-6.1.0-abc").mkdirs());
        ManagedPayloadStaging.stage(TestZips.write(tmp.newFile("p.zip"), payload()), updates(), "6.1.0");
        List<String> deleted = ManagedPayloadStaging.prune(updates(), "6.1.0");
        assertEquals(2, deleted.size());
        assertTrue(new File(updates(), "6.1.0").isDirectory());
        assertEquals(List.of("6.1.0"), List.of(updates().list()));
        ManagedPayloadStaging.prune(updates(), null);
        assertNothingStaged();
    }

    @Test
    public void readsTheInstalledVersion() throws Exception {
        File external = tmp.newFolder("files");
        assertNull(ManagedPayloadStaging.installedVersion(external));
        File manifest = new File(external, ManagedPayloadStaging.INSTALLED_MANIFEST);
        assertTrue(manifest.getParentFile().mkdirs());
        Files.write(manifest.toPath(), MANIFEST.getBytes(StandardCharsets.UTF_8));
        assertEquals("6.1.0", ManagedPayloadStaging.installedVersion(external));
    }

    @Test
    public void versionsCompareNumerically() {
        assertTrue(ManagedPayloadStaging.compareVersions("6.10.0", "6.9.9") > 0);
        assertEquals(0, ManagedPayloadStaging.compareVersions("6.1.0", "6.1.0"));
        assertTrue(ManagedPayloadStaging.compareVersions(null, "6.1.0") < 0);
    }

    @Test
    public void aRejectedStagingIsReportedOnTheNextLaunch() throws Exception {
        File state = new File(tmp.getRoot(), "managed-staged.txt");
        assertNull(ManagedPayloadStaging.remembered(state));
        ManagedPayloadStaging.remember(state, "6.1.0");
        assertEquals("6.1.0", ManagedPayloadStaging.remembered(state));

        assertTrue(new File(updates(), "6.1.0").mkdirs());
        assertFalse("still staged: the Updater has not run yet",
                ManagedPayloadStaging.lastInstallFailed("6.1.0", "6.0.0", updates()));
        ManagedPayloadStaging.prune(updates(), null);
        assertTrue("the Updater deleted it and kept the old Mods",
                ManagedPayloadStaging.lastInstallFailed("6.1.0", "6.0.0", updates()));
        assertTrue("a first install that failed", ManagedPayloadStaging.lastInstallFailed("6.1.0", null, updates()));
        assertFalse("swapped in", ManagedPayloadStaging.lastInstallFailed("6.1.0", "6.1.0", updates()));
        assertFalse("nothing staged", ManagedPayloadStaging.lastInstallFailed(null, "6.0.0", updates()));
        ManagedPayloadStaging.remember(state, null);
        assertNull(ManagedPayloadStaging.remembered(state));
    }
}
