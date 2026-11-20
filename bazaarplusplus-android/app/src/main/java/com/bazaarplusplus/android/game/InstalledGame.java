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

public final class InstalledGame {
    public static final String PACKAGE = "com.tempogames.thebazaar";
    public static final String PLAY_STORE = "com.android.vending";
    static final String INJECTED_LOADER_PREFIX = "assets/LemonLoader/";

    public final String versionName;
    public final long versionCode;
    public final long lastUpdateTime;
    public final ApplicationInfo info;
    public final List<String> apks;
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

    public static InstalledGame find(PackageManager pm) {
        return find(pm, PACKAGE);
    }

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

    static Set<String> signerDigests(List<byte[]> certificates) {
        Set<String> digests = new TreeSet<>();
        for (byte[] certificate : certificates) {
            Sha256 sha256 = new Sha256();
            sha256.update(certificate, 0, certificate.length);
            digests.add(sha256.hex());
        }
        return digests;
    }

    public static boolean playStoreInstalled(PackageManager pm) {
        try {
            pm.getPackageInfo(PLAY_STORE, 0);
            return true;
        } catch (PackageManager.NameNotFoundException e) {
            return false;
        }
    }

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
