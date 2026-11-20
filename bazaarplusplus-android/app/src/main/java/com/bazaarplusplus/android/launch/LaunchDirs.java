package com.bazaarplusplus.android.launch;

import java.io.File;

final class LaunchDirs {
    final File external;
    final File interopCache;
    final File managedPayloadCache;
    final File updates;
    final File stagedVersion;
    final File stagedGame;
    final File loaderDeployment;

    LaunchDirs(File external, File interopCache, File managedPayloadCache, File updates, File stagedVersion,
            File stagedGame, File loaderDeployment) {
        this.external = external;
        this.interopCache = interopCache;
        this.managedPayloadCache = managedPayloadCache;
        this.updates = updates;
        this.stagedVersion = stagedVersion;
        this.stagedGame = stagedGame;
        this.loaderDeployment = loaderDeployment;
    }
}
