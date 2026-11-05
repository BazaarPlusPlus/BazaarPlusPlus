package com.bazaarplusplus.android;

import android.content.ContextWrapper;
import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;
import org.json.JSONObject;
import java.io.File;
import java.nio.file.Files;
import java.nio.charset.StandardCharsets;
import static org.junit.Assert.*;

public class DevLoginTest {
    @Rule public TemporaryFolder temp = new TemporaryFolder();

    private ContextWrapper context() {
        return new ContextWrapper(null) {
            @Override public File getFilesDir() { return temp.getRoot(); }
        };
    }

    private File write(long expires) throws Exception {
        File file = new File(DevOverrides.loginDirectory(context()), "request.json");
        file.getParentFile().mkdirs();
        Files.write(file.toPath(), new JSONObject().put("id", "1234567890abcdef1234567890abcdef")
                .put("expiresAt", expires).put("password", "private").toString().getBytes(StandardCharsets.UTF_8));
        return file;
    }

    @Test public void liveRequestCanStartButExpiredRequestCannot() throws Exception {
        File request = write(System.currentTimeMillis() + 60_000);
        assertTrue(DevOverrides.loginRequested(context()));
        write(System.currentTimeMillis() - 60_000);
        assertFalse(DevOverrides.loginRequested(context()));
        assertFalse(request.exists());
    }

    @Test public void malformedAndOversizedRequestsNeverStart() throws Exception {
        File request = write(System.currentTimeMillis() + 60_000);
        for (String input : new String[] {"invalid", "x".repeat(16_385)}) {
            Files.write(request.toPath(), input.getBytes(StandardCharsets.UTF_8));
            assertFalse(DevOverrides.loginRequested(context()));
            assertFalse(request.exists());
        }
    }

    @Test public void blockedGateConsumesRequestAndWritesOnlyNonSecretReceipt() throws Exception {
        File request = write(System.currentTimeMillis() + 60_000);
        DevOverrides.rejectLogin(context(), "WAITING");
        assertFalse(request.exists());
        String text = new String(Files.readAllBytes(new File(request.getParentFile(), "result.json").toPath()), StandardCharsets.UTF_8);
        assertFalse(text.contains("password"));
        assertFalse(text.contains("private"));
        JSONObject result = new JSONObject(text);
        assertEquals("launch_blocked", result.getString("status"));
        assertEquals("WAITING", result.getString("phase"));
    }
}
