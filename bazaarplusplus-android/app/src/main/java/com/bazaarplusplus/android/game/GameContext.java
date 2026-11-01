package com.bazaarplusplus.android.game;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.pm.ApplicationInfo;
import android.content.res.AssetManager;
import android.content.res.Resources;

import java.io.File;

/**
 * The game's identity, code and resources over the App's Context: every writable path
 * (data, files, caches, external files) stays the App's own, so the game and LemonLoader write
 * only into the App's sandbox.
 */
final class GameContext extends ContextWrapper {
    private final InstalledGame game;
    private final Resources resources;
    private final ClassLoader loader;
    private final ApplicationInfo info;
    private Context applicationContext;

    GameContext(Context base, InstalledGame game, Resources resources, ClassLoader loader, File libraryDir) {
        super(base);
        this.game = game;
        this.resources = resources;
        this.loader = loader;
        info = new ApplicationInfo(game.info);
        ApplicationInfo app = base.getApplicationInfo();
        info.dataDir = app.dataDir;
        info.deviceProtectedDataDir = app.deviceProtectedDataDir;
        info.uid = app.uid;
        info.nativeLibraryDir = libraryDir.getAbsolutePath();
    }

    void setApplicationContext(Context context) {
        applicationContext = context;
    }

    @Override public Resources getResources() { return resources; }
    @Override public AssetManager getAssets() { return resources.getAssets(); }
    @Override public ClassLoader getClassLoader() { return loader; }
    @Override public String getPackageName() { return InstalledGame.PACKAGE; }
    @Override public ApplicationInfo getApplicationInfo() { return info; }
    @Override public String getPackageCodePath() { return game.info.sourceDir; }
    @Override public String getPackageResourcePath() { return game.info.sourceDir; }

    @Override
    public Context getApplicationContext() {
        return applicationContext != null ? applicationContext : this;
    }
}
