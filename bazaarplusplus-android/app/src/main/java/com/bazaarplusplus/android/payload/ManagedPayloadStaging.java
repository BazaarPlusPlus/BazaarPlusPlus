package com.bazaarplusplus.android.payload;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.release.SafeNames;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.List;
import java.util.UUID;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/** Android 11 lacks native Ed25519 support, so the Updater verifies the Managed Payload signature (ADR 0012). */
public final class ManagedPayloadStaging {
    public static final String UPDATES = "Updates";
    public static final String MANIFEST = "manifest.txt";
    public static final String SIGNATURE = "manifest.txt.minisig";
    public static final String INSTALLED_MANIFEST = "MelonLoader/Mods/BazaarPlusPlus.managed-manifest.txt";
    static final String DOWNLOAD_PREFIX = ".download-";
    static final String FILES = "files/";
    static final String[] INSTALL_DIRECTORIES = {"MelonLoader/Mods/", "MelonLoader/UserLibs/"};

    private ManagedPayloadStaging() {}

    static boolean allowedEntry(String name) {
        if (name.equals(MANIFEST) || name.equals(SIGNATURE)) return true;
        return name.startsWith(FILES) && installPath(name.substring(FILES.length()));
    }

    static boolean installPath(String path) {
        for (String directory : INSTALL_DIRECTORIES) {
            if (path.startsWith(directory)) return SafeNames.isFileName(path.substring(directory.length()));
        }
        return false;
    }

    public static List<String> removeInstalled(File externalFiles) throws IOException {
        File manifest = new File(externalFiles, INSTALLED_MANIFEST);
        String text = AtomicFiles.readUtf8(manifest);
        List<String> paths = new ArrayList<>();
        if (text == null) return paths;
        for (String line : text.split("\n")) {
            if (!line.startsWith("file ")) continue;
            String[] parts = line.split(" ", 4);
            if (parts.length != 4 || !installPath(parts[3])) {
                throw new IOException("The installed Managed Payload Manifest names " + line);
            }
            paths.add(parts[3]);
        }
        for (String path : paths) Files.deleteIfExists(new File(externalFiles, path).toPath());
        // Keep the manifest until every listed file is gone so an interrupted removal can resume.
        Files.deleteIfExists(manifest.toPath());
        return paths;
    }

    public static File stage(File zip, File updates, String version) throws IOException {
        if (!SafeNames.isFileName(version) || version.startsWith(".")) throw new IOException("Bad version " + version);
        try (ZipFile file = new ZipFile(zip)) {
            List<ZipEntry> entries = new ArrayList<>();
            boolean manifest = false;
            boolean signature = false;
            Enumeration<? extends ZipEntry> all = file.entries();
            while (all.hasMoreElements()) {
                ZipEntry entry = all.nextElement();
                if (entry.isDirectory() || !allowedEntry(entry.getName())) {
                    throw new IOException("The Managed Payload zip holds " + entry.getName());
                }
                manifest |= entry.getName().equals(MANIFEST);
                signature |= entry.getName().equals(SIGNATURE);
                entries.add(entry);
            }
            if (!manifest || !signature) throw new IOException("The Managed Payload zip has no signed manifest");
            String named = manifestVersion(read(file, file.getEntry(MANIFEST)));
            if (!version.equals(named)) throw new IOException("The Managed Payload manifest is version " + named);

            if (!updates.isDirectory() && !updates.mkdirs()) throw new IOException("Cannot create " + updates);
            File download = new File(updates, DOWNLOAD_PREFIX + version + "-" + UUID.randomUUID().toString().replace("-", ""));
            try {
                for (ZipEntry entry : entries) {
                    File target = new File(download, entry.getName());
                    File parent = target.getParentFile();
                    if (parent != null && !parent.isDirectory() && !parent.mkdirs()) throw new IOException("Cannot create " + parent);
                    try (InputStream in = file.getInputStream(entry); OutputStream out = new FileOutputStream(target)) {
                        byte[] buffer = new byte[1 << 16];
                        int read;
                        while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
                    }
                }
                File staged = new File(updates, version);
                if (staged.exists()) AtomicFiles.deleteTree(staged);
                if (!download.renameTo(staged)) throw new IOException("Cannot publish " + staged);
                return staged;
            } finally {
                if (download.exists()) AtomicFiles.deleteTree(download);
            }
        }
    }

    /** The Updater scans Updates/, so a stale staging could install Mods for the wrong interop. */
    public static List<String> prune(File updates, String keep) throws IOException {
        List<String> deleted = new ArrayList<>();
        File[] children = updates.listFiles();
        if (children == null) return deleted;
        for (File child : children) {
            if (child.getName().equals(keep)) continue;
            AtomicFiles.deleteTree(child);
            if (child.exists()) throw new IOException("Cannot delete " + child);
            deleted.add(child.getName());
        }
        return deleted;
    }

    public static String manifestVersion(String manifest) {
        if (manifest == null) return null;
        for (String line : manifest.split("\n")) {
            if (line.startsWith("version ")) return line.substring("version ".length()).trim();
        }
        return null;
    }

    public static String installedVersion(File externalFiles) {
        return manifestVersion(AtomicFiles.readUtf8(new File(externalFiles, INSTALLED_MANIFEST)));
    }

    public static int compareVersions(String a, String b) {
        long[] left = parse(a);
        long[] right = parse(b);
        if (left == null || right == null) return left == null ? (right == null ? 0 : -1) : 1;
        for (int i = 0; i < 3; i++) if (left[i] != right[i]) return Long.compare(left[i], right[i]);
        return 0;
    }

    /** The Updater deletes a rejected staging and lets the game start with its existing Mods. */
    public static boolean lastInstallFailed(String staged, String installed, File updates) {
        return staged != null && compareVersions(installed, staged) < 0 && !new File(updates, staged).isDirectory();
    }

    public static String remembered(File state) {
        String text = AtomicFiles.readUtf8(state);
        return text == null || text.trim().isEmpty() ? null : text.trim();
    }

    public static void remember(File state, String value) throws IOException {
        if (value == null) {
            Files.deleteIfExists(state.toPath());
        } else {
            AtomicFiles.writeUtf8(state, value + "\n");
        }
    }

    private static long[] parse(String version) {
        if (version == null) return null;
        String[] parts = version.split("\\.");
        if (parts.length != 3) return null;
        long[] numbers = new long[3];
        try {
            for (int i = 0; i < 3; i++) numbers[i] = Long.parseLong(parts[i]);
        } catch (NumberFormatException e) {
            return null;
        }
        return numbers;
    }

    private static String read(ZipFile file, ZipEntry entry) throws IOException {
        try (InputStream in = file.getInputStream(entry)) {
            return new String(AtomicFiles.readAtMost(in, 1 << 20), StandardCharsets.UTF_8);
        }
    }
}
