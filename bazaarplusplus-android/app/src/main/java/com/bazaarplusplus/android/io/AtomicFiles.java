package com.bazaarplusplus.android.io;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/** Small file operations the App's records share: whole-file reads and temp-file-then-rename writes. */
public final class AtomicFiles {
    private AtomicFiles() {}

    /** Writes {@code bytes} to a temporary sibling, then renames it over {@code target}. */
    public static void write(File target, byte[] bytes) throws IOException {
        File parent = target.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) throw new IOException("Cannot create " + parent);
        File tmp = new File(target.getPath() + ".tmp");
        try {
            Files.write(tmp.toPath(), bytes);
            if (!tmp.renameTo(target)) throw new IOException("Cannot replace " + target);
        } finally {
            if (tmp.exists()) tmp.delete();
        }
    }

    public static void writeUtf8(File target, String text) throws IOException {
        write(target, text.getBytes(StandardCharsets.UTF_8));
    }

    /** The file as UTF-8, or null when it does not exist or cannot be read. */
    public static String readUtf8(File file) {
        try {
            return new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8);
        } catch (IOException | RuntimeException e) {
            return null;
        }
    }

    /** The whole stream, refusing more than {@code limit} bytes (InputStream.readAllBytes needs API 33). */
    public static byte[] readAtMost(java.io.InputStream in, int limit) throws IOException {
        java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = in.read(buffer)) > 0) {
            if (out.size() + read > limit) throw new IOException("Larger than " + limit + " bytes");
            out.write(buffer, 0, read);
        }
        return out.toByteArray();
    }

    /** Deletes a file or a directory tree without following symbolic links. */
    public static void deleteTree(File file) {
        File[] children = Files.isSymbolicLink(file.toPath()) ? null : file.listFiles();
        if (children != null) for (File child : children) deleteTree(child);
        file.delete();
    }
}
