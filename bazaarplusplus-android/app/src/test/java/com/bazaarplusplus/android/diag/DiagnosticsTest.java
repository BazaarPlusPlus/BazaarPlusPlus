package com.bazaarplusplus.android.diag;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

public class DiagnosticsTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static void write(File file, String text) throws Exception {
        assertTrue(file.getParentFile().isDirectory() || file.getParentFile().mkdirs());
        Files.write(file.toPath(), text.getBytes(StandardCharsets.UTF_8));
    }

    @Test
    public void collectsTheLogsAndTheLaunchRecordAndRedactsThemInTheZip() throws Exception {
        File external = tmp.newFolder("files");
        File appLog = new File(tmp.getRoot(), "logs/app.log");
        write(new File(appLog.getPath() + ".1"), "old line\n");
        write(appLog, "new line\n");
        write(new File(external, "MelonLoader/MelonLoader/Latest.log"), "[Support Module Loaded] player@example.com\n");
        write(new File(external, "MelonLoader/UserData/BazaarPlusPlus.launch.json"), "{\"processId\": 1}\n");

        Map<String, String> files = Diagnostics.collect(external, appLog, "MelonLoader/UserData/BazaarPlusPlus.launch.json",
                "tier: NORMAL\n");
        assertEquals(List.of("state.txt", "app.log", "Latest.log", "BazaarPlusPlus.launch.json"),
                List.copyOf(files.keySet()));
        assertEquals("old line\nnew line\n", files.get("app.log"));

        ByteArrayOutputStream out = new ByteArrayOutputStream();
        Diagnostics.zip(files, out);
        Map<String, String> zipped = new HashMap<>();
        try (ZipInputStream zip = new ZipInputStream(new ByteArrayInputStream(out.toByteArray()))) {
            ZipEntry entry;
            while ((entry = zip.getNextEntry()) != null) {
                zipped.put(entry.getName(), new String(zip.readAllBytes(), StandardCharsets.UTF_8));
            }
        }
        assertEquals(4, zipped.size());
        assertFalse(zipped.get("Latest.log").contains("player@example.com"));
        assertTrue(zipped.get("Latest.log").contains("Support Module Loaded"));
    }

    @Test
    public void leavesOutWhatDoesNotExist() throws Exception {
        Map<String, String> files = Diagnostics.collect(tmp.newFolder("files"), new File(tmp.getRoot(), "none.log"),
                "MelonLoader/UserData/BazaarPlusPlus.launch.json", "tier: NOT_INSTALLED\n");
        assertEquals(List.of("state.txt"), List.copyOf(files.keySet()));
    }
}
