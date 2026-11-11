package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.io.File;
import java.security.PublicKey;
import java.util.List;

/**
 * The facts of one {@link LaunchCheck} that {@link BppLaunch#prepare} reads ({@link LaunchCheck#inputs}).
 * Plain values: a refused decision may come with any of them null, and {@code prepare} refuses it
 * before it reads one.
 */
final class LaunchInputs {
    final LaunchGate.Decision decision;
    /** Whether this build carries an Android App Runtime. */
    final boolean runtime;
    /** The installed game's Game Code Hash, taken by the check. */
    final GameCodeHash hash;
    final String gameVersionName;
    final long gameVersionCode;
    /** Null when no manifest was read; a NORMAL decision always has one. */
    final PlatformManifest manifest;
    /** Null when no Managed Payload is installed. */
    final String installedModsVersion;
    final List<PublicKey> trusted;
    /** A debug build's Development interop, or null. */
    final File devInterop;

    LaunchInputs(LaunchGate.Decision decision, boolean runtime, GameCodeHash hash, String gameVersionName,
            long gameVersionCode, PlatformManifest manifest, String installedModsVersion, List<PublicKey> trusted,
            File devInterop) {
        this.decision = decision;
        this.runtime = runtime;
        this.hash = hash;
        this.gameVersionName = gameVersionName;
        this.gameVersionCode = gameVersionCode;
        this.manifest = manifest;
        this.installedModsVersion = installedModsVersion;
        this.trusted = trusted;
        this.devInterop = devInterop;
    }
}
