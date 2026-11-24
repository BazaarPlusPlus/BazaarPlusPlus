package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.io.Sha256;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

public final class Download {
    static final int TIMEOUT_MS = 15_000;

    public interface Source {
        InputStream open() throws IOException;
    }

    public interface Progress {
        void update(long received, long total);
    }

    private Download() {}

    public static Source http(String url) {
        return http(url, false);
    }

    public static Source httpIdentity(String url) {
        return http(url, true);
    }

    private static Source http(String url, boolean identityEncoding) {
        return () -> {
            HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
            connection.setConnectTimeout(TIMEOUT_MS);
            connection.setReadTimeout(TIMEOUT_MS);
            if (identityEncoding) connection.setRequestProperty("Accept-Encoding", "identity");
            int status = connection.getResponseCode();
            if (status != HttpURLConnection.HTTP_OK) {
                connection.disconnect();
                throw new IOException("HTTP " + status + " for " + url);
            }
            return connection.getInputStream();
        };
    }

    public static String text(Source source, int limit) throws IOException {
        try (InputStream in = source.open()) {
            byte[] bytes = AtomicFiles.readAtMost(in, limit);
            return new String(bytes, StandardCharsets.UTF_8);
        }
    }

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
            Sha256 digest = new Sha256();
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
            String actual = digest.hex();
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
        return file.isFile() && file.length() == size && Sha256.of(file).equals(sha256);
    }
}
