package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.game.GameCodeHash;

import java.util.Objects;

/**
 * The launch rules of ADR 0012 ("三档启动规则"): which launches the installed game gets against the
 * Platform Release Manifest's {@code game}. Only the Game Code Hash and versionCode decide; a
 * versionName change with the same code is the same game.
 */
public final class LaunchGate {
    public enum Tier {
        /** The official game is not installed: guide the install; native launch is unavailable. */
        NOT_INSTALLED,
        /** The package holds a retired Android Release APK: guide the replacement; no launch runs it. */
        ANDROID_RELEASE_APK,
        /** No manifest from the network and none cached: native launch only. */
        OFFLINE_NO_CACHE,
        /** The installed game is the adapted one. */
        NORMAL,
        /** Newer code than the adapted game: native launch only until a Product Release adapts it. */
        NEWER_WAITING,
        /** Older code than the adapted game: update the official game first. */
        OLDER
    }

    /** The installed game as the gate sees it. */
    public static final class Installed {
        public final long versionCode;
        public final GameCodeHash hash;
        /** The game's APKs carry an injected loader: its seed files would replace the Mods the App stages. */
        public final boolean androidReleaseApk;

        public Installed(long versionCode, GameCodeHash hash, boolean androidReleaseApk) {
            this.versionCode = versionCode;
            this.hash = Objects.requireNonNull(hash);
            this.androidReleaseApk = androidReleaseApk;
        }
    }

    /** The game a release adapts: the Platform Release Manifest's {@code game}. */
    public static final class Adapted {
        public final long versionCode;
        public final GameCodeHash hash;

        public Adapted(long versionCode, GameCodeHash hash) {
            this.versionCode = versionCode;
            this.hash = Objects.requireNonNull(hash);
        }
    }

    public static final class Inputs {
        /** Null when the official game is not installed. */
        public final Installed installed;
        /** Null when no manifest could be read and none is cached. */
        public final Adapted adapted;

        public Inputs(Installed installed, Adapted adapted) {
            this.installed = installed;
            this.adapted = adapted;
        }
    }

    public static final class Decision {
        public final Tier tier;

        Decision(Tier tier) {
            this.tier = tier;
        }

        /** Whether the BPP launch button launches now. */
        public boolean bppLaunch() {
            return tier == Tier.NORMAL;
        }

        /** Native launch opens the official App untouched; it needs only the game installed. */
        public boolean nativeLaunch() {
            return tier != Tier.NOT_INSTALLED && tier != Tier.ANDROID_RELEASE_APK;
        }

        @Override
        public String toString() {
            return tier.toString();
        }
    }

    private LaunchGate() {}

    public static Decision decide(Inputs in) {
        if (in.installed == null) return new Decision(Tier.NOT_INSTALLED);
        // Whatever the release says: the copy's code hash can match, and its seed files still win.
        if (in.installed.androidReleaseApk) return new Decision(Tier.ANDROID_RELEASE_APK);
        if (in.adapted == null) return new Decision(Tier.OFFLINE_NO_CACHE);
        if (in.installed.hash.equals(in.adapted.hash)) return new Decision(Tier.NORMAL);
        // Different code with the same versionCode counts as newer (ADR 0012).
        if (in.installed.versionCode < in.adapted.versionCode) return new Decision(Tier.OLDER);
        return new Decision(Tier.NEWER_WAITING);
    }
}
