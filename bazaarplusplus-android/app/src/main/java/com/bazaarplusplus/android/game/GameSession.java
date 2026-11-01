package com.bazaarplusplus.android.game;

import android.annotation.SuppressLint;
import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.content.res.AssetManager;
import android.content.res.Resources;
import android.content.res.loader.ResourcesLoader;
import android.content.res.loader.ResourcesProvider;
import android.os.ParcelFileDescriptor;
import android.util.Log;

import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.util.List;
import java.util.Map;

import dalvik.system.PathClassLoader;

/**
 * One prepared game for this process: one class loader over bridge.dex and the game's APKs, the
 * game's Resources with the LemonLoader runtime and the Interop Package added as resource loaders,
 * and a links directory for the game's native libraries. The bridge Activity reaches
 * {@link #wrap} by name, so the method stays public and static.
 */
public final class GameSession {
    public static final String TAG = "BppGame";
    public static final String GAME_ACTIVITY = "com.bazaarplusplus.android.game.GameActivity";

    /** What a BPP launch hands the game: read-only files in the App's internal storage. */
    public static final class Inputs {
        public final File bridgeDex;
        public final File runtimeZip;
        public final File interopZip;

        public Inputs(File bridgeDex, File runtimeZip, File interopZip) {
            this.bridgeDex = bridgeDex;
            this.runtimeZip = runtimeZip;
            this.interopZip = interopZip;
        }
    }

    // One prepared game per process, held for the process's life: the bridge Activity finds it
    // by name. wrappedApp wraps the application Context, never an Activity.
    private static volatile boolean prepared;
    private static InstalledGame game;
    private static Resources gameResources;
    private static ClassLoader gameLoader;
    @SuppressLint("StaticFieldLeak")
    private static Context wrappedApp;
    private static File libraryDir;

    private GameSession() {}

    /** Prepares the installed game once per process; later calls must name the same inputs. */
    public static synchronized void prepare(Context context, InstalledGame installed, Inputs inputs) throws Exception {
        if (prepared) return;
        Context app = context.getApplicationContext() != null ? context.getApplicationContext() : context;
        game = installed;
        ApplicationInfo info = game.info;
        Log.i(TAG, "game " + game.versionName + " (" + game.versionCode + ") apks=" + game.apks.size()
                + " nativeLibraryDir=" + info.nativeLibraryDir);

        StringBuilder dexPath = new StringBuilder(inputs.bridgeDex.getAbsolutePath());
        for (String apk : game.apks) dexPath.append(File.pathSeparator).append(apk);
        File appLibraryDir = new File(app.getApplicationInfo().nativeLibraryDir);
        File gameLibraryDir = new File(info.nativeLibraryDir);
        libraryDir = linkLibraries(app, appLibraryDir, gameLibraryDir);
        String libraryPath = LibraryLinks.librarySearchPath(libraryDir, appLibraryDir, gameLibraryDir, game.apks);
        gameLoader = new PathClassLoader(dexPath.toString(), libraryPath, GameSession.class.getClassLoader());
        Log.i(TAG, "game loader dex=" + dexPath + " lib=" + libraryPath);

        gameResources = app.createPackageContext(InstalledGame.PACKAGE, 0).getResources();
        addLoader(gameResources, inputs.runtimeZip);
        if (inputs.interopZip != null) addLoader(gameResources, inputs.interopZip);
        AssetManager assets = gameResources.getAssets();
        try (InputStream probe = assets.open("LemonLoader/payload.json")) {
            Log.i(TAG, "LemonLoader runtime visible through the game's assets");
        }
        wrappedApp = new GameContext(app, game, gameResources, gameLoader, libraryDir);
        prepared = true;
    }

    public static ClassLoader loader() {
        if (!prepared) throw new IllegalStateException("The game is not prepared");
        return gameLoader;
    }

    /** The game's Context over the App's: called by the bridge Activity's attachBaseContext. */
    public static Context wrap(Context base) {
        if (!prepared) throw new IllegalStateException("The game is not prepared");
        GameContext wrapped = new GameContext(base, game, gameResources, gameLoader, libraryDir);
        wrapped.setApplicationContext(wrappedApp);
        return wrapped;
    }

    private static void addLoader(Resources resources, File zip) throws IOException {
        ResourcesLoader loader = new ResourcesLoader();
        try (ParcelFileDescriptor fd = ParcelFileDescriptor.open(zip, ParcelFileDescriptor.MODE_READ_ONLY)) {
            loader.addProvider(ResourcesProvider.loadFromApk(fd));
        }
        resources.addLoaders(loader);
        Log.i(TAG, "added " + zip.getName() + " (" + zip.length() + " bytes) to the game's resources");
    }

    private static File linkLibraries(Context app, File appLibraryDir, File gameLibraryDir) throws Exception {
        File dir = new File(app.getNoBackupFilesDir(), "game-libs/" + game.versionCode + "-" + game.lastUpdateTime);
        String[] own = appLibraryDir.list();
        Map<String, File> targets = LibraryLinks.plan(LibraryLinks.libraryNames(game.apks), gameLibraryDir,
                own == null ? List.of() : List.of(own), appLibraryDir);
        File parent = dir.getParentFile();
        File[] stale = parent == null ? null : parent.listFiles();
        if (stale != null) for (File old : stale) if (!old.equals(dir)) deleteTree(old);
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Cannot create " + dir);
        for (Map.Entry<String, File> e : targets.entrySet()) {
            File link = new File(dir, e.getKey());
            link.delete();
            android.system.Os.symlink(e.getValue().getAbsolutePath(), link.getAbsolutePath());
        }
        Log.i(TAG, "linked " + targets.size() + " libraries into " + dir);
        return dir;
    }

    static void deleteTree(File file) {
        File[] children = file.listFiles();
        if (children != null && !isSymlink(file)) for (File child : children) deleteTree(child);
        file.delete();
    }

    private static boolean isSymlink(File file) {
        try {
            return java.nio.file.Files.isSymbolicLink(file.toPath());
        } catch (SecurityException e) {
            return false;
        }
    }
}
