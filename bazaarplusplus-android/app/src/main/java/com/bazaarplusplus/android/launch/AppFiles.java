package com.bazaarplusplus.android.launch;

import android.content.Context;

import java.io.File;
import java.io.IOException;

/**
 * Where the launcher keeps its records. Internal storage holds what only the App reads; the
 * external files directory is shared with LemonLoader and the mod (MelonLoader/, the Data Root).
 */
final class AppFiles {
    /** The Data Root's name: the mod's PathConstants.DataRootDirectoryName (root AGENTS.md, Contracts). */
    static final String DATA_ROOT = "BazaarPlusPlusV6";

    private AppFiles() {}

    static File external(Context context) throws IOException {
        File dir = context.getExternalFilesDir(null);
        if (dir == null) throw new IOException("The external files directory is not available");
        return dir;
    }

    static File updates(Context context) throws IOException {
        return new File(external(context), DATA_ROOT + "/" + com.bazaarplusplus.android.payload.ManagedPayloadStaging.UPDATES);
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

    static File compatibilityChoices(Context context) {
        return new File(context.getFilesDir(), "compatibility-mode.json");
    }

    static File stagedVersion(Context context) {
        return new File(context.getFilesDir(), "managed-staged.txt");
    }

    static File loaderDeployment(Context context) {
        return new File(context.getNoBackupFilesDir(), "loader-deployment.json");
    }
}
