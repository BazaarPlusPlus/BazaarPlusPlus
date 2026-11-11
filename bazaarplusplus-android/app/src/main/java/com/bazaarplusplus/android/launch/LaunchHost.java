package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.io.File;

/**
 * What {@link BppLaunch} needs from Android, and nothing else: files and their paths come through
 * {@link LaunchDirs}, the launch's facts through {@link LaunchInputs}. {@link AndroidLaunchHost} is
 * the App's implementation; a JVM test records the calls.
 */
interface LaunchHost {
    /** Whether this process already prepared the game ({@code GameSession}). */
    boolean gamePrepared();

    /** The bytes of {@code file}: the copy the APK carries, else the network. Opening it is the download. */
    Download.Source source(PlatformManifest.Artifact file);

    /** This App's versionCode, which names the loader deployment's identity. */
    long appVersionCode();

    /** This App as the Launch Record names it. */
    LaunchRecord.App app();

    /** Prepares the installed game in this process with this App's runtime files and {@code interopZip}. */
    void prepareGame(File interopZip) throws Exception;

    /** This process, which the Updater matches against the Launch Record. */
    long processId();
}
