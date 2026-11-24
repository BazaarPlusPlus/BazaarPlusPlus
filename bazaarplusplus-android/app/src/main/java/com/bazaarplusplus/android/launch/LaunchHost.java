package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.io.File;

interface LaunchHost {
    boolean gamePrepared();

    Download.Source source(PlatformManifest.Artifact file);

    default boolean hasCarried(PlatformManifest.Artifact file) {
        return false;
    }

    default Download.Source urlSource(String url, boolean identityEncoding) {
        return identityEncoding ? Download.httpIdentity(url) : Download.http(url);
    }

    long appVersionCode();

    LaunchRecord.App app();

    void prepareGame(File interopZip) throws Exception;

    /** The Updater compares this PID with the Launch Record to reject records from earlier processes. */
    long processId();
}
