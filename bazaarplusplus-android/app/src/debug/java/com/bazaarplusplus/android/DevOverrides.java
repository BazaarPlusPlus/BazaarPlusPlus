package com.bazaarplusplus.android;

import android.content.Context;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.release.ReleaseOrigin;

import java.io.File;
import java.io.FileInputStream;
import java.nio.charset.StandardCharsets;
import org.json.JSONObject;
import java.util.List;

/** The release source set omits these hooks so production builds cannot read development overrides. */
public final class DevOverrides {
    private DevOverrides() {}

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


    /** The device reaches the development server through {@code adb reverse}. */
    public static ReleaseOrigin origin(Context context) {
        String port = read(context, "manifest-port");
        if (port == null || !port.matches("[0-9]{1,5}")) return null;
        return new ReleaseOrigin("http://127.0.0.1:" + port);
    }

    public static List<File> trustedCertificates(Context context) {
        File cert = new File(context.getFilesDir(), "dev/trusted-cert.der");
        return cert.isFile() ? List.of(cert) : List.of();
    }

    public static File interop(Context context) {
        File zip = new File(context.getFilesDir(), "dev/interop.zip");
        return zip.isFile() ? zip : null;
    }

    public static String gamePackage(Context context) {
        return read(context, "game-package");
    }

    private static String read(Context context, String name) {
        String text = AtomicFiles.readUtf8(new File(context.getFilesDir(), "dev/" + name));
        return text == null || text.trim().isEmpty() ? null : text.trim();
    }
}
