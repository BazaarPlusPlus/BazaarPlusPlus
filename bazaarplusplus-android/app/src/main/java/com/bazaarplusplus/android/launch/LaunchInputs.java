package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.io.File;
import java.security.PublicKey;
import java.util.List;

final class LaunchInputs {
    final LaunchGate.Decision decision;
    final boolean runtime;
    final GameCodeHash hash;
    final String gameVersionName;
    final long gameVersionCode;
    final PlatformManifest manifest;
    final PlatformManifest.Adaptation adaptation;
    final String installedModsVersion;
    final List<PublicKey> trusted;
    final File devInterop;

    LaunchInputs(LaunchGate.Decision decision, boolean runtime, GameCodeHash hash, String gameVersionName,
            long gameVersionCode, PlatformManifest manifest, PlatformManifest.Adaptation adaptation,
            String installedModsVersion, List<PublicKey> trusted, File devInterop) {
        this.decision = decision;
        this.runtime = runtime;
        this.hash = hash;
        this.gameVersionName = gameVersionName;
        this.gameVersionCode = gameVersionCode;
        this.manifest = manifest;
        this.adaptation = adaptation;
        this.installedModsVersion = installedModsVersion;
        this.trusted = trusted;
        this.devInterop = devInterop;
    }
}
