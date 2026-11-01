package com.bazaarplusplus.android;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;

/** The App's own version, read from its package so the manifest stays the one owner. */
public final class BuildConfigFacts {
    private BuildConfigFacts() {}

    public static PackageInfo self(Context context) {
        try {
            return context.getPackageManager().getPackageInfo(context.getPackageName(), 0);
        } catch (PackageManager.NameNotFoundException e) {
            throw new IllegalStateException("The App cannot read its own package", e);
        }
    }

    public static String versionName(Context context) {
        return self(context).versionName;
    }

    public static long versionCode(Context context) {
        return self(context).getLongVersionCode();
    }
}
