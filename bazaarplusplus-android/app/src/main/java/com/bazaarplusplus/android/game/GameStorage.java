package com.bazaarplusplus.android.game;

import android.content.Context;

import java.io.File;

// On 16 KB page devices, fuse-bpf can truncate Android/data listings and hide interop assemblies from LemonLoader.
public final class GameStorage {
    private GameStorage() {}

    public static File root(Context context) {
        return new File(context.getNoBackupFilesDir(), "game-external");
    }
}
