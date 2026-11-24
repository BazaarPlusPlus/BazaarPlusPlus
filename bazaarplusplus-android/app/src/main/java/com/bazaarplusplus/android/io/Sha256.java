package com.bazaarplusplus.android.io;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;

public final class Sha256 {
    private final MessageDigest digest;

    public Sha256() {
        try {
            digest = MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    public void update(byte[] buffer, int offset, int length) {
        digest.update(buffer, offset, length);
    }

    /** Reading the digest resets the accumulator. */
    public String hex() {
        byte[] bytes = digest.digest();
        StringBuilder hex = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) hex.append(Character.forDigit((b >> 4) & 0xf, 16)).append(Character.forDigit(b & 0xf, 16));
        return hex.toString();
    }

    public static String of(InputStream in) throws IOException {
        Sha256 sha256 = new Sha256();
        byte[] buffer = new byte[1 << 16];
        int read;
        while ((read = in.read(buffer)) > 0) sha256.update(buffer, 0, read);
        return sha256.hex();
    }

    public static String of(byte[] bytes, int offset, int length) {
        Sha256 sha256 = new Sha256();
        sha256.update(bytes, offset, length);
        return sha256.hex();
    }

    public static String of(byte[] bytes) {
        return of(bytes, 0, bytes.length);
    }

    public static String of(File file) throws IOException {
        try (InputStream in = new FileInputStream(file)) {
            return of(in);
        }
    }
}
