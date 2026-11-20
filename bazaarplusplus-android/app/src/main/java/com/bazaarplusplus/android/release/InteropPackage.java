package com.bazaarplusplus.android.release;

import java.io.File;
import java.io.IOException;
import java.util.Enumeration;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/** A later resource loader can shadow runtime assets, so interop entries must stay under {@link #ENTRY_ROOT}. */
public final class InteropPackage {
    public static final String ENTRY_ROOT = "assets/LemonLoader/runtime/interop/";

    private InteropPackage() {}

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
