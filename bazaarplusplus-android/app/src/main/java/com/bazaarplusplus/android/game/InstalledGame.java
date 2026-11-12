package com.bazaarplusplus.android.game;

import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.Signature;

import com.bazaarplusplus.android.io.Sha256;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import java.util.TreeSet;
import java.util.zip.ZipFile;

/**
 * The official game as PackageManager reports it: identity, signing certificates and every APK it
 * is installed from.
 */
public final class InstalledGame {
    public static final String PACKAGE = "com.tempogames.thebazaar";
    /** The Google Play Store, which installs and updates the official game where it exists. */
    public static final String PLAY_STORE = "com.android.vending";
    /** Where an Android Release APK carries its loader and seed Mods; the official game has no such entry. */
    static final String INJECTED_LOADER_PREFIX = "assets/LemonLoader/";

    public final String versionName;
    public final long versionCode;
    public final long lastUpdateTime;
    public final ApplicationInfo info;
    /** base.apk first, then every split, in PackageManager's order. */
    public final List<String> apks;
    /**
     * The sha256 of each certificate that signs the game's APKs: the Android source's signer
     * (ADR 0019). Empty when PackageManager reports none.
     */
    public final Set<String> signers;

    InstalledGame(String versionName, long versionCode, long lastUpdateTime, ApplicationInfo info, List<String> apks,
            Set<String> signers) {
        this.versionName = versionName;
        this.versionCode = versionCode;
        this.lastUpdateTime = lastUpdateTime;
        this.info = info;
        this.apks = Collections.unmodifiableList(apks);
        this.signers = Collections.unmodifiableSet(signers);
    }

    /** The installed game, or null when it is not installed. */
    public static InstalledGame find(PackageManager pm) {
        return find(pm, PACKAGE);
    }

    /** The package installed as {@code packageName}; a debug build may name another one (DevOverrides). */
    public static InstalledGame find(PackageManager pm, String packageName) {
        PackageInfo game;
        try {
            game = pm.getPackageInfo(packageName, PackageManager.GET_SIGNING_CERTIFICATES);
        } catch (PackageManager.NameNotFoundException e) {
            return null;
        }
        List<byte[]> certificates = new ArrayList<>();
        if (game.signingInfo != null) {
            for (Signature signer : game.signingInfo.getApkContentsSigners()) certificates.add(signer.toByteArray());
        }
        return new InstalledGame(game.versionName, game.getLongVersionCode(), game.lastUpdateTime,
                game.applicationInfo, apkPaths(game.applicationInfo.sourceDir, game.applicationInfo.splitSourceDirs),
                signerDigests(certificates));
    }

    /** The sha256 of each DER-encoded certificate, as a manifest's {@code signerSha256} names one. */
    static Set<String> signerDigests(List<byte[]> certificates) {
        Set<String> digests = new TreeSet<>();
        for (byte[] certificate : certificates) {
            Sha256 sha256 = new Sha256();
            sha256.update(certificate, 0, certificate.length);
            digests.add(sha256.hex());
        }
        return digests;
    }

    /** Whether the Google Play Store is on this device. */
    public static boolean playStoreInstalled(PackageManager pm) {
        try {
            pm.getPackageInfo(PLAY_STORE, 0);
            return true;
        } catch (PackageManager.NameNotFoundException e) {
            return false;
        }
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
