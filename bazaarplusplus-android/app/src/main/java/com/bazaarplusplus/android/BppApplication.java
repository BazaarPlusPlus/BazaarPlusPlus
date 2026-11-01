package com.bazaarplusplus.android;

import android.app.Application;

import com.bazaarplusplus.android.diag.AppLog;

/** The App process. The game runs in this process too, under the App's UID and sandbox. */
public final class BppApplication extends Application {
    public static final String TAG = "BppApp";

    @Override
    public void onCreate() {
        super.onCreate();
        AppLog.init(this);
        AppLog.i(TAG, "process " + android.os.Process.myPid() + " started, version " + BuildConfigFacts.versionName(this));
    }
}
