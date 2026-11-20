package com.bazaarplusplus.android.game;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.pm.ApplicationInfo;
import android.content.res.AssetManager;
import android.content.res.Resources;

import java.io.File;

/** The game sees its package identity, while writable paths must stay within the App's UID and sandbox. */
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
    public File getExternalFilesDir(String type) {
        File dir = type == null ? GameStorage.root(getBaseContext()) : new File(GameStorage.root(getBaseContext()), type);
        dir.mkdirs();
        return dir;
    }

    @Override
    public File[] getExternalFilesDirs(String type) {
        return new File[] { getExternalFilesDir(type) };
    }

    @Override
    public Context getApplicationContext() {
        return applicationContext != null ? applicationContext : this;
    }
}
