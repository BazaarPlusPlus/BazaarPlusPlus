package com.bazaarplusplus.android.game;

import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.zip.ZipFile;

/** The official game as PackageManager reports it: identity and every APK it is installed from. */
public final class InstalledGame {
    public static final String PACKAGE = "com.tempogames.thebazaar";
    /** Where an Android Release APK carries its loader and seed Mods; the official game has no such entry. */
    static final String INJECTED_LOADER_PREFIX = "assets/LemonLoader/";

    public final String versionName;
    public final long versionCode;
    public final long lastUpdateTime;
    public final ApplicationInfo info;
    /** base.apk first, then every split, in PackageManager's order. */
    public final List<String> apks;

    InstalledGame(String versionName, long versionCode, long lastUpdateTime, ApplicationInfo info, List<String> apks) {
        this.versionName = versionName;
        this.versionCode = versionCode;
        this.lastUpdateTime = lastUpdateTime;
        this.info = info;
        this.apks = Collections.unmodifiableList(apks);
    }

    /** The installed game, or null when it is not installed. */
    public static InstalledGame find(PackageManager pm) {
        return find(pm, PACKAGE);
    }

    /** The package installed as {@code packageName}; a debug build may name another one (DevOverrides). */
    public static InstalledGame find(PackageManager pm, String packageName) {
        PackageInfo game;
        try {
            game = pm.getPackageInfo(packageName, 0);
        } catch (PackageManager.NameNotFoundException e) {
            return null;
        }
        return new InstalledGame(game.versionName, game.getLongVersionCode(), game.lastUpdateTime,
                game.applicationInfo, apkPaths(game.applicationInfo.sourceDir, game.applicationInfo.splitSourceDirs));
    }

    /** Whether this is a retired Android Release APK rather than the official game. */
    public boolean carriesInjectedLoader() throws IOException {
        return carriesInjectedLoader(apks);
    }

    static boolean carriesInjectedLoader(List<String> apks) throws IOException {
        for (String apk : apks) {
            try (ZipFile zip = new ZipFile(new File(apk))) {
                if (zip.stream().anyMatch(entry -> entry.getName().startsWith(INJECTED_LOADER_PREFIX))) return true;
            }
        }
        return false;
    }

    static List<String> apkPaths(String sourceDir, String[] splitSourceDirs) {
        List<String> apks = new ArrayList<>();
        apks.add(sourceDir);
        if (splitSourceDirs != null) Collections.addAll(apks, splitSourceDirs);
        return apks;
    }
}
