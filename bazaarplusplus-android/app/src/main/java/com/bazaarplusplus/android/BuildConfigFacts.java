package com.bazaarplusplus.android;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;

/** Read package metadata so the Android manifest remains the source of the App version. */
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
