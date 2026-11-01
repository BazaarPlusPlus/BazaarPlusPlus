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

/**
 * Stages a Managed Payload zip for the Updater (ADR 0012, "下载与 Managed Payload 的安装"), in the
 * layout of Core's {@code ManagedPayloadStager}: {@code Updates/.download-<version>-<random>/}
 * first, then one rename to {@code Updates/<version>/}. The App checks only the zip's sha256 (the
 * download did) and its entry names; the Updater verifies the minisign signature and each file.
 */
public final class ManagedPayloadStaging {
    public static final String UPDATES = "Updates";
    public static final String MANIFEST = "manifest.txt";
    public static final String SIGNATURE = "manifest.txt.minisig";
    /** The installed Managed Payload Manifest beside the mod, relative to the external files directory. */
    public static final String INSTALLED_MANIFEST = "MelonLoader/Mods/BazaarPlusPlus.managed-manifest.txt";
    static final String DOWNLOAD_PREFIX = ".download-";
    static final String[] FILE_DIRECTORIES = {"files/MelonLoader/Mods/", "files/MelonLoader/UserLibs/"};

    private ManagedPayloadStaging() {}

    /** Whether the zip may hold {@code name}: the manifest, its signature, or one file in Mods/ or UserLibs/. */
    static boolean allowedEntry(String name) {
        if (name.equals(MANIFEST) || name.equals(SIGNATURE)) return true;
        for (String directory : FILE_DIRECTORIES) {
            if (name.startsWith(directory)) return SafeNames.isFileName(name.substring(directory.length()));
        }
        return false;
    }

    /**
     * Extracts {@code zip} into {@code updates/<version>/}, replacing an earlier staging of the same
     * version. Any entry outside the allowed names, or a manifest of another version, rejects the
     * whole zip and leaves nothing behind.
     */
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

    /**
     * Deletes everything in {@code updates/} except {@code keep} (null keeps nothing), so the
     * Updater swaps in only the Mods this launch staged. Returns the names deleted.
     */
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

    /** The {@code version} line of a Managed Payload Manifest, or null. */
    public static String manifestVersion(String manifest) {
        if (manifest == null) return null;
        for (String line : manifest.split("\n")) {
            if (line.startsWith("version ")) return line.substring("version ".length()).trim();
        }
        return null;
    }

    /** The installed Managed Payload's version, or null when none is installed. */
    public static String installedVersion(File externalFiles) {
        return manifestVersion(AtomicFiles.readUtf8(new File(externalFiles, INSTALLED_MANIFEST)));
    }

    /** Numeric x.y.z order; an unparsable or missing version sorts first. */
    public static int compareVersions(String a, String b) {
        long[] left = parse(a);
        long[] right = parse(b);
        if (left == null || right == null) return left == null ? (right == null ? 0 : -1) : 1;
        for (int i = 0; i < 3; i++) if (left[i] != right[i]) return Long.compare(left[i], right[i]);
        return 0;
    }

    /**
     * ADR 0012: the Updater deletes a staging it rejects, and the game starts without new Mods.
     * True when the App staged {@code staged} for an earlier launch, the installed Mods are still
     * older, and the staging directory is gone.
     */
    public static boolean lastInstallFailed(String staged, String installed, File updates) {
        return staged != null && compareVersions(installed, staged) < 0 && !new File(updates, staged).isDirectory();
    }

    /** The version this App last staged, kept in its internal storage. */
    public static String remembered(File state) {
        String text = AtomicFiles.readUtf8(state);
        return text == null || text.trim().isEmpty() ? null : text.trim();
    }

    public static void remember(File state, String version) throws IOException {
        if (version == null) {
            Files.deleteIfExists(state.toPath());
        } else {
            AtomicFiles.writeUtf8(state, version + "\n");
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
