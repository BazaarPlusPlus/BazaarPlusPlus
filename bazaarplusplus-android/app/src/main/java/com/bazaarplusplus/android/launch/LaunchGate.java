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
        /** No manifest from the network and none cached: native launch only. */
        OFFLINE_NO_CACHE,
        /** The manifest has no Android App release yet (the retired injected-APK shape): native launch only. */
        NO_RELEASE,
        /** The release's Managed Payload needs another Android App runtime: install the new App first. */
        APP_UPDATE_REQUIRED,
        /** The installed game is the adapted one. */
        NORMAL,
        /** Newer code with a usable Pre-Adaptation: BPP launch in Compatibility Mode after the user opts in. */
        NEWER_COMPAT_AVAILABLE,
        /** Newer code and no usable Pre-Adaptation yet: native launch only. */
        NEWER_WAITING,
        /** Older code than the adapted game: update the official game first. */
        OLDER
    }

    /** The installed game as the gate sees it. */
    public static final class Installed {
        public final long versionCode;
        public final GameCodeHash hash;

        public Installed(long versionCode, GameCodeHash hash) {
            this.versionCode = versionCode;
            this.hash = Objects.requireNonNull(hash);
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
        /** Null when no manifest could be read and none is cached, or when {@link #noRelease}. */
        public final Adapted adapted;
        /** The manifest decoded as "no Android App release yet". */
        public final boolean noRelease;
        /** A Pre-Adaptation whose hashes, Loader Release and signature all fit the installed game. */
        public final boolean preAdaptationUsable;
        /** The user opted in to Compatibility Mode for the installed Game Code Hash. */
        public final boolean compatibilityChosen;
        /** The manifest's nonManagedPayloadDigest differs from this App's own (never for a dev build). */
        public final boolean appUpdateRequired;

        public Inputs(Installed installed, Adapted adapted, boolean noRelease, boolean preAdaptationUsable,
                boolean compatibilityChosen, boolean appUpdateRequired) {
            this.installed = installed;
            this.adapted = adapted;
            this.noRelease = noRelease;
            this.preAdaptationUsable = preAdaptationUsable;
            this.compatibilityChosen = compatibilityChosen;
            this.appUpdateRequired = appUpdateRequired;
        }
    }

    public static final class Decision {
        public final Tier tier;
        /** For {@link Tier#NEWER_COMPAT_AVAILABLE}: the user already opted in for this Game Code Hash. */
        public final boolean compatibilityChosen;

        Decision(Tier tier, boolean compatibilityChosen) {
            this.tier = tier;
            this.compatibilityChosen = compatibilityChosen;
        }

        /** Whether the BPP launch button launches now. */
        public boolean bppLaunch() {
            return tier == Tier.NORMAL || (tier == Tier.NEWER_COMPAT_AVAILABLE && compatibilityChosen);
        }

        /** Whether this launch would run in Compatibility Mode. */
        public boolean compatibility() {
            return tier == Tier.NEWER_COMPAT_AVAILABLE;
        }

        /** Native launch opens the official App untouched; it needs only the game installed. */
        public boolean nativeLaunch() {
            return tier != Tier.NOT_INSTALLED;
        }

        @Override
        public String toString() {
            return tier + (tier == Tier.NEWER_COMPAT_AVAILABLE ? " chosen=" + compatibilityChosen : "");
        }
    }

    private LaunchGate() {}

    public static Decision decide(Inputs in) {
        if (in.installed == null) return new Decision(Tier.NOT_INSTALLED, false);
        if (in.noRelease) return new Decision(Tier.NO_RELEASE, false);
        if (in.adapted == null) return new Decision(Tier.OFFLINE_NO_CACHE, false);
        // The release's Managed Payload cannot run on this App's runtime, whatever the game.
        if (in.appUpdateRequired) return new Decision(Tier.APP_UPDATE_REQUIRED, false);
        if (in.installed.hash.equals(in.adapted.hash)) return new Decision(Tier.NORMAL, false);
        // Different code with the same versionCode counts as newer (ADR 0012).
        if (in.installed.versionCode < in.adapted.versionCode) return new Decision(Tier.OLDER, false);
        if (!in.preAdaptationUsable) return new Decision(Tier.NEWER_WAITING, false);
        return new Decision(Tier.NEWER_COMPAT_AVAILABLE, in.compatibilityChosen);
    }
}
