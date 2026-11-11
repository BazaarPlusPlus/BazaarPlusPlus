package com.bazaarplusplus.android.launch;

import android.content.Context;

import com.bazaarplusplus.android.game.GameStorage;

import java.io.File;
import java.io.IOException;

/**
 * Where the launcher keeps its records. Internal storage holds what only the App reads; the
 * game storage is shared with LemonLoader and the mod (MelonLoader/, the Data Root).
 */
final class AppFiles {
    /** The Data Root's name: the mod's PathConstants.DataRootDirectoryName (root AGENTS.md, Contracts). */
    static final String DATA_ROOT = "BazaarPlusPlusV6";

    private AppFiles() {}

    static File external(Context context) throws IOException {
        File dir = GameStorage.root(context);
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Cannot create " + dir);
        return dir;
    }

    static File updates(Context context) throws IOException {
        return updates(external(context));
    }

    /** The Data Root's {@code Updates/} under the external files directory {@code external}. */
    static File updates(File external) {
        return new File(external, DATA_ROOT + "/" + com.bazaarplusplus.android.payload.ManagedPayloadStaging.UPDATES);
    }

    static File releaseCache(Context context, boolean development) {
        return new File(context.getFilesDir(), development ? "release-dev" : "release");
    }

    static File interopCache(Context context) {
        return new File(context.getNoBackupFilesDir(), "interop");
    }

    static File managedPayloadCache(Context context) {
        return new File(context.getNoBackupFilesDir(), "managed");
    }

    static File gameCodeHash(Context context) {
        return new File(context.getNoBackupFilesDir(), "game-code-hash.json");
    }

    static File stagedVersion(Context context) {
        return new File(context.getFilesDir(), "managed-staged.txt");
    }

    static File loaderDeployment(Context context) {
        return new File(context.getNoBackupFilesDir(), "loader-deployment.json");
    }

    /** A BPP launch's directories. Paths only: unlike {@link #external}, it creates nothing. */
    static LaunchDirs launchDirs(Context context) {
        File external = GameStorage.root(context);
        return new LaunchDirs(external, interopCache(context), managedPayloadCache(context), updates(external),
                stagedVersion(context), loaderDeployment(context));
    }
}
