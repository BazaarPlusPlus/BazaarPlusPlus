package com.bazaarplusplus.android.launch;

import java.io.File;

/**
 * Where a BPP launch reads and writes ({@link AppFiles#launchDirs} for the App). Paths only: none
 * of them needs to exist, and {@link BppLaunch#prepare} creates what it writes.
 */
final class LaunchDirs {
    /** The game storage: LemonLoader's {@code MelonLoader/} and the Launch Record live under it. */
    final File external;
    final File interopCache;
    final File managedPayloadCache;
    /** The Data Root's {@code Updates/}, where the Updater finds a staged Managed Payload. */
    final File updates;
    /** The Staged version's state file. */
    final File stagedVersion;
    /** The state file of the game the installed Mods were staged for. */
    final File stagedGame;
    /** {@code LoaderDeployment}'s state file. */
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
