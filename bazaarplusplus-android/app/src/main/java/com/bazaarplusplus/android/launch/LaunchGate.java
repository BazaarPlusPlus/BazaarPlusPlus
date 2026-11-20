package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.game.GameCodeHash;

import java.util.List;
import java.util.Objects;
import java.util.Set;

public final class LaunchGate {
    public enum Tier {
        NOT_INSTALLED,
        ANDROID_RELEASE_APK,
        OFFLINE_NO_CACHE,
        NORMAL,
        NEWER_WAITING,
        OLDER
    }

    public static final class Installed {
        public final long versionCode;
        public final GameCodeHash hash;
        public final Set<String> signers;
        public final boolean androidReleaseApk;

        public Installed(long versionCode, GameCodeHash hash, Set<String> signers, boolean androidReleaseApk) {
            this.versionCode = versionCode;
            this.hash = Objects.requireNonNull(hash);
            this.signers = Objects.requireNonNull(signers);
            this.androidReleaseApk = androidReleaseApk;
        }
    }

    public static final class Adapted {
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
        public final Installed installed;
        public final List<Adapted> adaptations;
        public final boolean developmentInterop;

        public Inputs(Installed installed, List<Adapted> adaptations, boolean developmentInterop) {
            this.installed = installed;
            this.adaptations = adaptations;
            this.developmentInterop = developmentInterop;
        }
    }

    public static final class Decision {
        public final Tier tier;
        public final Adapted adaptation;
        public final boolean developmentInterop;

        Decision(Tier tier, Adapted adaptation, boolean developmentInterop) {
            this.tier = tier;
            this.adaptation = adaptation;
            this.developmentInterop = developmentInterop;
        }

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
        // An injected APK can match the code hash, but LemonLoader would deploy its seed files over the App's Mods.
        if (in.installed.androidReleaseApk) return new Decision(Tier.ANDROID_RELEASE_APK, null, dev);
        if (in.adaptations == null) return new Decision(Tier.OFFLINE_NO_CACHE, null, dev);
        for (Adapted adapted : in.adaptations) {
            if (in.installed.hash.equals(adapted.hash)) return new Decision(Tier.NORMAL, adapted, dev);
        }
        // Android sources ship different builds, so their version codes are comparable only within a source.
        for (Adapted adapted : in.adaptations) {
            if (!in.installed.signers.contains(adapted.signerSha256)) continue;
            Tier tier = in.installed.versionCode < adapted.versionCode ? Tier.OLDER : Tier.NEWER_WAITING;
            return new Decision(tier, adapted, dev);
        }
        return new Decision(Tier.NEWER_WAITING, null, dev);
    }
}
