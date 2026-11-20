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

// Unity loads libmain.so by absolute path from nativeLibraryDir, so it needs the links directory.
// The APK search paths handle libraries that Android did not extract.
public final class LibraryLinks {
    static final String ABI_DIR = "lib/arm64-v8a/";

    private LibraryLinks() {}

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

    public static String librarySearchPath(File links, File appLibraryDir, File gameLibraryDir, List<String> apks) {
        StringBuilder path = new StringBuilder(links.getAbsolutePath())
                .append(File.pathSeparator).append(appLibraryDir.getAbsolutePath())
                .append(File.pathSeparator).append(gameLibraryDir.getAbsolutePath());
        for (String apk : apks) path.append(File.pathSeparator).append(apk).append("!/").append(ABI_DIR, 0, ABI_DIR.length() - 1);
        return path.toString();
    }
}
