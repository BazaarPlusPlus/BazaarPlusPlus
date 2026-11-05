package com.bazaarplusplus.android;

import android.content.Context;

import com.bazaarplusplus.android.release.ReleaseOrigin;

import java.io.File;
import java.util.List;

/** A release build has no development hooks; the debug source set has the real ones. */
public final class DevOverrides {
    private DevOverrides() {}

    public static File loginDirectory(Context context) {
        return null;
    }

    public static boolean loginRequested(Context context) {
        return false;
    }

    public static void rejectLogin(Context context, String phase) {}


    public static ReleaseOrigin origin(Context context) {
        return null;
    }

    public static List<File> trustedCertificates(Context context) {
        return List.of();
    }

    public static File interop(Context context) {
        return null;
    }

    public static String gamePackage(Context context) {
        return null;
    }
}
