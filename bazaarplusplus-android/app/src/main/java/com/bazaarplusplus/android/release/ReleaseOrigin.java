package com.bazaarplusplus.android.release;

import java.net.URI;
import java.net.URISyntaxException;
import java.util.Objects;
import java.util.regex.Pattern;

public final class ReleaseOrigin {
    public static final String PLATFORM_KEY = "android-aarch64";
    public static final ReleaseOrigin PUBLIC = new ReleaseOrigin("https://cdn.bazaarplusplus.com/installer");
    static final Pattern LOADER_TAG = Pattern.compile("[A-Za-z0-9][A-Za-z0-9._-]*");

    public final String base;

    public ReleaseOrigin(String base) {
        this.base = Objects.requireNonNull(base).replaceAll("/+$", "");
    }

    public String manifestUrl() {
        return base + "/latest/mod/" + PLATFORM_KEY + ".json";
    }

    public String interopUrl(String zipSha256) {
        return base + "/android/interop/" + zipSha256 + ".zip";
    }

    /** The release tooling owns this key in release/manifest.ts {@code androidManagedPayloadKey}. */
    public String managedPayloadUrl(String version, String source) {
        return base + "/" + version + "/" + PLATFORM_KEY + "/managed/" + source + "/BazaarPlusPlus_" + version + "_"
                + PLATFORM_KEY + "-" + source + "-managed.zip";
    }

    /** The release tooling owns this key in release/manifest.ts {@code androidManagedPayloadChunkIndexKey}. */
    public String managedPayloadChunkIndexUrl(String version, String source) {
        return base + "/" + version + "/" + PLATFORM_KEY + "/managed/" + source + "/BazaarPlusPlus_" + version + "_"
                + PLATFORM_KEY + "-" + source + "-managed.chunks.txt";
    }

    /** The release tooling owns this prefix in release/managed-payload-chunks.ts {@code MANAGED_CHUNKS_PREFIX}. */
    public String managedChunksBaseUrl() {
        return base + "/managed/chunks";
    }

    public String managedChunkUrl(String compressedSha256) {
        if (compressedSha256 == null || !StrictJson.SHA256.matcher(compressedSha256).matches()) {
            throw new IllegalArgumentException("Invalid chunk sha256");
        }
        return managedChunksBaseUrl() + "/" + compressedSha256.substring(0, 2) + "/" + compressedSha256 + ".deflate";
    }

    boolean under(String url, String prefix) {
        URI uri;
        URI root;
        try {
            uri = new URI(url);
            root = new URI(base + prefix);
        } catch (URISyntaxException e) {
            return false;
        }
        if (uri.getRawUserInfo() != null || uri.getRawQuery() != null || uri.getRawFragment() != null) return false;
        if (!Objects.equals(uri.getScheme(), root.getScheme()) || !Objects.equals(uri.getHost(), root.getHost())
                || uri.getPort() != root.getPort()) {
            return false;
        }
        String path = uri.getRawPath();
        String rootPath = root.getRawPath();
        return path != null && path.startsWith(rootPath) && path.length() > rootPath.length()
                && !path.contains("/../") && !path.contains("/./");
    }
}
