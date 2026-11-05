package com.bazaarplusplus.android;

import android.content.Context;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.release.ReleaseOrigin;

import java.io.File;
import java.io.FileInputStream;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;
import java.util.List;

/**
 * Debug-build hooks a developer sets with run-as under the App's internal {@code files/dev/}
 * (android.just has the recipes). The release source set has the same class answering "none",
 * so a release build cannot read any of these files.
 */
public final class DevOverrides {
    private DevOverrides() {}

    /** Private development login mailbox; release builds never expose one to the game. */
    public static File loginDirectory(Context context) {
        return new File(context.getFilesDir(), "dev/login");
    }

    public static boolean loginRequested(Context context) {
        File request = new File(loginDirectory(context), "request.json");
        if (!request.isFile()) return false;
        try {
            JSONObject value = readLoginRequest(request);
            long now = System.currentTimeMillis();
            long expires = value.getLong("expiresAt");
            if (value.getString("id").matches("[0-9a-f]{32}") && expires > now && expires <= now + 600_000)
                return true;
            rejectLogin(context, "expired_request");
        } catch (Exception ignored) {
            // A malformed request must never launch the game or leave credentials behind.
            request.delete();
        }
        return false;
    }

    /** A settled non-Play launch gate fails the request without changing that gate. */
    public static void rejectLogin(Context context, String phase) {
        File directory = loginDirectory(context);
        File request = new File(directory, "request.json");
        try {
            JSONObject value = readLoginRequest(request);
            if (!request.delete()) return;
            JSONObject result = new JSONObject()
                    .put("id", value.optString("id"))
                    .put("pid", android.os.Process.myPid())
                    .put("status", "launch_blocked")
                    .put("phase", phase)
                    .put("submitted", false);
            AtomicFiles.writeUtf8(new File(directory, "result.json"), result.toString());
        } catch (Exception ignored) {
            request.delete();
        }
    }

    private static JSONObject readLoginRequest(File file) throws Exception {
        try (FileInputStream input = new FileInputStream(file)) {
            return new JSONObject(new String(AtomicFiles.readAtMost(input, 16_384), StandardCharsets.UTF_8));
        }
    }


    /** {@code files/dev/manifest-port}: read releases from {@code http://127.0.0.1:<port>} (adb reverse). */
    public static ReleaseOrigin origin(Context context) {
        String port = read(context, "manifest-port");
        if (port == null || !port.matches("[0-9]{1,5}")) return null;
        return new ReleaseOrigin("http://127.0.0.1:" + port);
    }

    /** {@code files/dev/trusted-cert.der}: one more certificate for interop statements, for a throwaway key. */
    public static List<File> trustedCertificates(Context context) {
        File cert = new File(context.getFilesDir(), "dev/trusted-cert.der");
        return cert.isFile() ? List.of(cert) : List.of();
    }

    /** {@code files/dev/interop.zip}: a Development interop that skips the manifest (just android::push-interop). */
    public static File interop(Context context) {
        File zip = new File(context.getFilesDir(), "dev/interop.zip");
        return zip.isFile() ? zip : null;
    }

    /** {@code files/dev/game-package}: look up another package as the official game, to show the not-installed state. */
    public static String gamePackage(Context context) {
        return read(context, "game-package");
    }

    private static String read(Context context, String name) {
        String text = AtomicFiles.readUtf8(new File(context.getFilesDir(), "dev/" + name));
        return text == null || text.trim().isEmpty() ? null : text.trim();
    }
}
