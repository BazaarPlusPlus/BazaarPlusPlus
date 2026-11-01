package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.io.AtomicFiles;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;

/**
 * Every download of ADR 0012 ("下载与 Managed Payload 的安装"): written to a temporary file, checked
 * for size and sha256, then renamed into place read-only. A file that fails a check is deleted.
 */
public final class Download {
    static final int TIMEOUT_MS = 15_000;

    /** Opens the bytes to download. */
    public interface Source {
        InputStream open() throws IOException;
    }

    /** Bytes received so far of {@code total}. */
    public interface Progress {
        void update(long received, long total);
    }

    private Download() {}

    public static Source http(String url) {
        return () -> {
            HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
            connection.setConnectTimeout(TIMEOUT_MS);
            connection.setReadTimeout(TIMEOUT_MS);
            int status = connection.getResponseCode();
            if (status != HttpURLConnection.HTTP_OK) {
                connection.disconnect();
                throw new IOException("HTTP " + status + " for " + url);
            }
            return connection.getInputStream();
        };
    }

    /** A small text document, at most {@code limit} bytes, as UTF-8. */
    public static String text(Source source, int limit) throws IOException {
        try (InputStream in = source.open()) {
            byte[] bytes = AtomicFiles.readAtMost(in, limit);
            return new String(bytes, StandardCharsets.UTF_8);
        }
    }

    /**
     * {@code target} when it already holds the expected bytes; otherwise downloads into it. The
     * cached copy is hashed again, so a corrupted cache is downloaded anew.
     */
    public static File cachedOrFetch(Source source, long size, String sha256, File target, Progress progress)
            throws IOException {
        if (matches(target, size, sha256)) return target;
        return fetch(source, size, sha256, target, progress);
    }

    public static File fetch(Source source, long size, String sha256, File target, Progress progress) throws IOException {
        File parent = target.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) throw new IOException("Cannot create " + parent);
        File tmp = new File(target.getPath() + ".download");
        tmp.delete();
        boolean done = false;
        try {
            MessageDigest digest = sha256Digest();
            long received = 0;
            try (InputStream in = source.open(); OutputStream out = new FileOutputStream(tmp)) {
                byte[] buffer = new byte[1 << 16];
                int read;
                while ((read = in.read(buffer)) > 0) {
                    received += read;
                    if (received > size) throw new IOException("Received more than the " + size + " bytes expected");
                    digest.update(buffer, 0, read);
                    out.write(buffer, 0, read);
                    if (progress != null) progress.update(received, size);
                }
            }
            if (received != size) throw new IOException("Received " + received + " of " + size + " bytes");
            String actual = hex(digest.digest());
            if (!actual.equals(sha256)) throw new IOException("sha256 " + actual + " is not the expected " + sha256);
            target.setWritable(true);
            target.delete();
            if (!tmp.renameTo(target)) throw new IOException("Cannot move " + tmp + " to " + target);
            target.setWritable(false, false);
            done = true;
            return target;
        } finally {
            if (!done) tmp.delete();
        }
    }

    public static boolean matches(File file, long size, String sha256) throws IOException {
        if (!file.isFile() || file.length() != size) return false;
        MessageDigest digest = sha256Digest();
        try (InputStream in = new FileInputStream(file)) {
            byte[] buffer = new byte[1 << 16];
            int read;
            while ((read = in.read(buffer)) > 0) digest.update(buffer, 0, read);
        }
        return hex(digest.digest()).equals(sha256);
    }

    private static MessageDigest sha256Digest() {
        try {
            return MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    private static String hex(byte[] bytes) {
        StringBuilder hex = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) hex.append(Character.forDigit((b >> 4) & 0xf, 16)).append(Character.forDigit(b & 0xf, 16));
        return hex.toString();
    }
}
