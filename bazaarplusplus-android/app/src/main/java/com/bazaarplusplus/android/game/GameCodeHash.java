package com.bazaarplusplus.android.game;

import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.List;
import java.util.Objects;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/**
 * The Game Code Hash (root GLOSSARY.md): the sha256 of the game's libil2cpp.so and
 * global-metadata.dat, taken from whichever of its APKs holds each (a Play install splits them
 * between base.apk and split_config.arm64_v8a.apk). Release tooling reads the same two entries
 * (release/android-apk.mjs, ANDROID_APK_ENTRIES).
 */
public final class GameCodeHash {
    public static final String IL2CPP_ENTRY = "lib/arm64-v8a/libil2cpp.so";
    public static final String METADATA_ENTRY = "assets/bin/Data/Managed/Metadata/global-metadata.dat";

    public final String il2cppSha256;
    public final String metadataSha256;

    public GameCodeHash(String il2cppSha256, String metadataSha256) {
        this.il2cppSha256 = Objects.requireNonNull(il2cppSha256);
        this.metadataSha256 = Objects.requireNonNull(metadataSha256);
    }

    /** Hashes both entries across the given APKs; fails when either is missing or appears twice. */
    public static GameCodeHash compute(List<String> apks) throws IOException {
        String il2cpp = null;
        String metadata = null;
        for (String apk : apks) {
            try (ZipFile zip = new ZipFile(new File(apk))) {
                String found = hashEntry(zip, IL2CPP_ENTRY);
                if (found != null) {
                    if (il2cpp != null) throw new IOException(IL2CPP_ENTRY + " appears in more than one APK");
                    il2cpp = found;
                }
                found = hashEntry(zip, METADATA_ENTRY);
                if (found != null) {
                    if (metadata != null) throw new IOException(METADATA_ENTRY + " appears in more than one APK");
                    metadata = found;
                }
            }
        }
        if (il2cpp == null) throw new IOException("No APK of the game holds " + IL2CPP_ENTRY);
        if (metadata == null) throw new IOException("No APK of the game holds " + METADATA_ENTRY);
        return new GameCodeHash(il2cpp, metadata);
    }

    public boolean matches(String il2cppSha256, String metadataSha256) {
        return this.il2cppSha256.equals(il2cppSha256) && this.metadataSha256.equals(metadataSha256);
    }

    @Override
    public boolean equals(Object other) {
        return other instanceof GameCodeHash && matches(((GameCodeHash) other).il2cppSha256,
                ((GameCodeHash) other).metadataSha256);
    }

    @Override
    public int hashCode() {
        return Objects.hash(il2cppSha256, metadataSha256);
    }

    @Override
    public String toString() {
        return il2cppSha256 + "_" + metadataSha256;
    }

    private static String hashEntry(ZipFile zip, String name) throws IOException {
        ZipEntry entry = zip.getEntry(name);
        if (entry == null) return null;
        try (InputStream in = zip.getInputStream(entry)) {
            return Sha256.of(in);
        }
    }

    /** Lowercase hex sha256 of a stream. */
    public static final class Sha256 {
        private Sha256() {}

        public static String of(InputStream in) throws IOException {
            MessageDigest digest;
            try {
                digest = MessageDigest.getInstance("SHA-256");
            } catch (NoSuchAlgorithmException e) {
                throw new IllegalStateException(e);
            }
            byte[] buffer = new byte[1 << 16];
            int read;
            while ((read = in.read(buffer)) > 0) digest.update(buffer, 0, read);
            StringBuilder hex = new StringBuilder(64);
            for (byte b : digest.digest()) hex.append(String.format("%02x", b));
            return hex.toString();
        }
    }
}
