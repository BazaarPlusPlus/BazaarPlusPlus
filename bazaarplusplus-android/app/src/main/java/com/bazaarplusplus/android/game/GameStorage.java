package com.bazaarplusplus.android.game;

import android.content.Context;

import java.io.File;

/**
 * What the game and LemonLoader see as the external files directory. It is in internal storage
 * because fuse-bpf truncates directory listings under Android/data on 16 KB page devices, which
 * hides interop assemblies from LemonLoader (ADR 0012, "游戏存储").
 */
public final class GameStorage {
    private GameStorage() {}

    public static File root(Context context) {
        return new File(context.getNoBackupFilesDir(), "game-external");
    }
}
