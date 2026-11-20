package com.bazaarplusplus.android.launch;

import android.content.Context;

import com.bazaarplusplus.android.game.GameStorage;

import java.io.File;
import java.io.IOException;

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

    static File stagedGame(Context context) {
        return new File(context.getFilesDir(), "managed-staged-game.txt");
    }

    static File loaderDeployment(Context context) {
        return new File(context.getNoBackupFilesDir(), "loader-deployment.json");
    }

    static LaunchDirs launchDirs(Context context) {
        File external = GameStorage.root(context);
        return new LaunchDirs(external, interopCache(context), managedPayloadCache(context), updates(external),
                stagedVersion(context), stagedGame(context), loaderDeployment(context));
    }
}
