package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.game.GameCodeHash;

import java.net.URI;
import java.net.URISyntaxException;
import java.util.Objects;
import java.util.regex.Pattern;

/**
 * Where the App reads releases: the public bucket's {@code installer/} prefix (release/downloads.ts
 * {@code RELEASE_BASE_URL}). A debug build may point it at a local server instead (DevOverrides).
 * Every artifact URL a manifest or record names must lie below it.
 */
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

    /** The Pre-Adaptation record of one game code for one Loader Release. */
    public String preAdaptationUrl(GameCodeHash game, String loaderTag) {
        if (!LOADER_TAG.matcher(loaderTag).matches()) throw new IllegalArgumentException("Bad loader tag " + loaderTag);
        return base + "/android/adaptations/" + game.il2cppSha256 + "_" + game.metadataSha256 + "/" + loaderTag + ".json";
    }

    /** The content-addressed Interop Package of one zip sha256. */
    public String interopUrl(String zipSha256) {
        return base + "/android/interop/" + zipSha256 + ".zip";
    }

    /** True when {@code url} is a plain URL (no user, query or fragment) strictly below {@code base + prefix}. */
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
