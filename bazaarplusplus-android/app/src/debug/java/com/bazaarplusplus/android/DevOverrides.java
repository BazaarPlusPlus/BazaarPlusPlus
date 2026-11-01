package com.bazaarplusplus.android;

import android.content.Context;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.release.ReleaseOrigin;

import java.io.File;
import java.util.List;

/**
 * Debug-build hooks a developer sets with run-as under the App's internal {@code files/dev/}
 * (android.just has the recipes). The release source set has the same class answering "none",
 * so a release build cannot read any of these files.
 */
public final class DevOverrides {
    private DevOverrides() {}

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
