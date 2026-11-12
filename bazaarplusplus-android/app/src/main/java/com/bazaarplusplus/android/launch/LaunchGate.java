package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.game.GameCodeHash;

import java.util.List;
import java.util.Objects;
import java.util.Set;

/**
 * The launch rules of ADR 0019 ("启动门禁：只和同一来源比较"): which launches the installed game gets
 * against the Platform Release Manifest's adaptations. A Game Code Hash that matches any adaptation
 * selects it; otherwise the installed game's signer picks its source's adaptation, and only that
 * one's versionCode is compared. A versionName change with the same code is the same game.
 */
public final class LaunchGate {
    public enum Tier {
        /** The official game is not installed: guide the install; native launch is unavailable. */
        NOT_INSTALLED,
        /** The package holds a retired Android Release APK: guide the replacement; no launch runs it. */
        ANDROID_RELEASE_APK,
        /** No manifest from the network and none cached: native launch only. */
        OFFLINE_NO_CACHE,
        /** The installed game is an adapted one. */
        NORMAL,
        /**
         * Newer code than its source's adapted game, or a source no adaptation names: native launch
         * only until a Product Release adapts it.
         */
        NEWER_WAITING,
        /** Older code than its source's adapted game: update the official game first. */
        OLDER
    }

    /** The installed game as the gate sees it. */
    public static final class Installed {
        public final long versionCode;
        public final GameCodeHash hash;
        /** The sha256 of each signing certificate; empty when they could not be read. */
        public final Set<String> signers;
        /** The game's APKs carry an injected loader: its seed files would replace the Mods the App stages. */
        public final boolean androidReleaseApk;

        public Installed(long versionCode, GameCodeHash hash, Set<String> signers, boolean androidReleaseApk) {
            this.versionCode = versionCode;
            this.hash = Objects.requireNonNull(hash);
            this.signers = Objects.requireNonNull(signers);
            this.androidReleaseApk = androidReleaseApk;
        }
    }

    /** The game one adaptation of the Platform Release Manifest adapts, and its source's signer. */
    public static final class Adapted {
        /** The adaptation's key in the manifest: its Android source. */
        public final String source;
        public final String signerSha256;
        public final long versionCode;
        public final GameCodeHash hash;

        public Adapted(String source, String signerSha256, long versionCode, GameCodeHash hash) {
            this.source = Objects.requireNonNull(source);
            this.signerSha256 = Objects.requireNonNull(signerSha256);
            this.versionCode = versionCode;
            this.hash = Objects.requireNonNull(hash);
        }
    }

    public static final class Inputs {
        /** Null when the official game is not installed. */
        public final Installed installed;
        /** Every adaptation in manifest order; null when no manifest could be read and none is cached. */
        public final List<Adapted> adaptations;
        /** A debug build's Development interop exists; a release build never has one. */
        public final boolean developmentInterop;

        public Inputs(Installed installed, List<Adapted> adaptations, boolean developmentInterop) {
            this.installed = installed;
            this.adaptations = adaptations;
            this.developmentInterop = developmentInterop;
        }
    }

    public static final class Decision {
        public final Tier tier;
        /**
         * The adaptation the tier was decided against: the hash match for NORMAL, the installed
         * game's source for OLDER and NEWER_WAITING. Null for any other tier and for a source no
         * adaptation names.
         */
        public final Adapted adaptation;
        /** The Development interop of {@link Inputs}; it never changes the tier. */
        public final boolean developmentInterop;

        Decision(Tier tier, Adapted adaptation, boolean developmentInterop) {
            this.tier = tier;
            this.adaptation = adaptation;
            this.developmentInterop = developmentInterop;
        }

        /**
         * Whether a BPP launch may run: the one rule the launcher and {@code BppLaunch.prepare} ask.
         * A Development interop also opens the tiers that only lack an adapted release, because the
         * developer's interop matches their snapshot lock. It never opens the Android Release APK
         * tier: LemonLoader would deploy the copy's seed files over the Mods.
         */
        public boolean bppLaunch() {
            switch (tier) {
                case NORMAL:
                    return true;
                case OFFLINE_NO_CACHE:
                case NEWER_WAITING:
                case OLDER:
                    return developmentInterop;
                default:
                    return false;
            }
        }

        /** Native launch opens the official App untouched; it needs only the game installed. */
        public boolean nativeLaunch() {
            return tier != Tier.NOT_INSTALLED && tier != Tier.ANDROID_RELEASE_APK;
        }

        @Override
        public String toString() {
            return adaptation == null ? tier.toString() : tier + " (" + adaptation.source + ")";
        }
    }

    private LaunchGate() {}

    public static Decision decide(Inputs in) {
        boolean dev = in.developmentInterop;
        if (in.installed == null) return new Decision(Tier.NOT_INSTALLED, null, dev);
        // Whatever the release says: the copy's code hash can match, and its seed files still win.
        if (in.installed.androidReleaseApk) return new Decision(Tier.ANDROID_RELEASE_APK, null, dev);
        if (in.adaptations == null) return new Decision(Tier.OFFLINE_NO_CACHE, null, dev);
        // The code decides first, so a game whose signers cannot be read still launches.
        for (Adapted adapted : in.adaptations) {
            if (in.installed.hash.equals(adapted.hash)) return new Decision(Tier.NORMAL, adapted, dev);
        }
        // Only the same source's versionCode compares: the sources do not ship the same builds.
        for (Adapted adapted : in.adaptations) {
            if (!in.installed.signers.contains(adapted.signerSha256)) continue;
            // Different code with the same versionCode counts as newer (ADR 0012).
            Tier tier = in.installed.versionCode < adapted.versionCode ? Tier.OLDER : Tier.NEWER_WAITING;
            return new Decision(tier, adapted, dev);
        }
        return new Decision(Tier.NEWER_WAITING, null, dev);
    }
}
