package com.bazaarplusplus.android.release;

import java.io.File;
import java.io.IOException;
import java.util.Enumeration;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/**
 * The Interop Package's layout (ADR 0012, "发布产物"): each top-level file of an Interop Snapshot
 * at {@code assets/LemonLoader/runtime/interop/<name>}, nothing else. A resource loader added
 * after the runtime would shadow any runtime asset of the same name, so the App refuses a zip
 * with any other entry before it adds it to the game's resources.
 */
public final class InteropPackage {
    public static final String ENTRY_ROOT = "assets/LemonLoader/runtime/interop/";

    private InteropPackage() {}

    /** The first entry that does not belong, or null when every entry is an interop file. */
    public static String strayEntry(File zip) throws IOException {
        try (ZipFile file = new ZipFile(zip)) {
            Enumeration<? extends ZipEntry> entries = file.entries();
            if (!entries.hasMoreElements()) return "(no entries)";
            while (entries.hasMoreElements()) {
                String name = entries.nextElement().getName();
                if (!allowed(name)) return name;
            }
        }
        return null;
    }

    static boolean allowed(String name) {
        if (!name.startsWith(ENTRY_ROOT)) return false;
        return SafeNames.isFileName(name.substring(ENTRY_ROOT.length()));
    }
}
