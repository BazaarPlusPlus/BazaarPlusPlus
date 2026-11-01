package com.bazaarplusplus.android.game;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/**
 * Unity loads libmain.so by absolute path from nativeLibraryDir, so the game gets a directory of
 * links: the App's own libraries (LemonLoader's libmain.so and libe_sqlite3.so) and every other
 * library from the game's extracted libs. A game library that was not extracted (a split APK
 * installed with extractNativeLibs=false) gets no link; the class loader finds it inside its APK
 * through the "apk!/lib/arm64-v8a" entries of {@link #librarySearchPath}.
 */
public final class LibraryLinks {
    static final String ABI_DIR = "lib/arm64-v8a/";

    private LibraryLinks() {}

    /** Every library name to the file its link points at; the App's libraries win. */
    public static Map<String, File> plan(List<String> gameLibraries, File gameLibraryDir, List<String> appLibraries,
            File appLibraryDir) {
        TreeMap<String, File> targets = new TreeMap<>();
        for (String name : gameLibraries) {
            File target = new File(gameLibraryDir, name);
            if (target.isFile()) targets.put(name, target);
        }
        for (String name : appLibraries) targets.put(name, new File(appLibraryDir, name));
        return targets;
    }

    /** The arm64 library names inside the given APKs. */
    public static List<String> libraryNames(List<String> apks) throws IOException {
        List<String> names = new ArrayList<>();
        for (String apk : apks) {
            try (ZipFile zip = new ZipFile(apk)) {
                Enumeration<? extends ZipEntry> entries = zip.entries();
                while (entries.hasMoreElements()) {
                    String name = entries.nextElement().getName();
                    if (name.startsWith(ABI_DIR) && name.endsWith(".so") && name.indexOf('/', ABI_DIR.length()) < 0) {
                        names.add(name.substring(ABI_DIR.length()));
                    }
                }
            }
        }
        return names;
    }

    /** The links directory first, then the App's and the game's library dirs, then each APK's libs. */
    public static String librarySearchPath(File links, File appLibraryDir, File gameLibraryDir, List<String> apks) {
        StringBuilder path = new StringBuilder(links.getAbsolutePath())
                .append(File.pathSeparator).append(appLibraryDir.getAbsolutePath())
                .append(File.pathSeparator).append(gameLibraryDir.getAbsolutePath());
        for (String apk : apks) path.append(File.pathSeparator).append(apk).append("!/").append(ABI_DIR, 0, ABI_DIR.length() - 1);
        return path.toString();
    }
}
