package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.launch.LaunchGate.Tier;

import org.junit.Test;

public class LaunchGateTest {
    static final GameCodeHash ADAPTED = new GameCodeHash("a".repeat(64), "b".repeat(64));
    static final GameCodeHash OTHER = new GameCodeHash("c".repeat(64), "d".repeat(64));
    static final LaunchGate.Adapted RELEASE = new LaunchGate.Adapted(12825, ADAPTED);

    static LaunchGate.Decision decide(LaunchGate.Installed installed, LaunchGate.Adapted adapted, boolean preAdaptation,
            boolean chosen) {
        return LaunchGate.decide(new LaunchGate.Inputs(installed, adapted, false, preAdaptation, chosen));
    }

    static LaunchGate.Installed installed(long versionCode, GameCodeHash hash) {
        return new LaunchGate.Installed(versionCode, hash, false);
    }

    @Test
    public void anAndroidReleaseApkGetsNoLaunchEvenWithTheAdaptedCode() {
        LaunchGate.Installed copy = new LaunchGate.Installed(12825, ADAPTED, true);
        LaunchGate.Decision decision = decide(copy, RELEASE, false, false);
        assertEquals(Tier.ANDROID_RELEASE_APK, decision.tier);
        assertFalse(decision.bppLaunch());
        assertFalse(decision.nativeLaunch());
        assertEquals(Tier.ANDROID_RELEASE_APK,
                LaunchGate.decide(new LaunchGate.Inputs(copy, null, true, false, false)).tier);
    }

    @Test
    public void matchingCodeIsNormal() {
        LaunchGate.Decision decision = decide(installed(12825, ADAPTED), RELEASE, false, false);
        assertEquals(Tier.NORMAL, decision.tier);
        assertTrue(decision.bppLaunch());
        assertFalse(decision.compatibility());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void aContentOnlyUpdateWithTheSameCodeStaysNormal() {
        // Only the Game Code Hash decides: a higher versionCode with the adapted code is normal.
        assertEquals(Tier.NORMAL, decide(installed(12900, ADAPTED), RELEASE, false, false).tier);
    }

    @Test
    public void newerCodeWithAPreAdaptationOffersCompatibilityModeUntilChosen() {
        LaunchGate.Decision offered = decide(installed(12900, OTHER), RELEASE, true, false);
        assertEquals(Tier.NEWER_COMPAT_AVAILABLE, offered.tier);
        assertFalse("the user must opt in first", offered.bppLaunch());
        assertTrue(offered.nativeLaunch());

        LaunchGate.Decision chosen = decide(installed(12900, OTHER), RELEASE, true, true);
        assertEquals(Tier.NEWER_COMPAT_AVAILABLE, chosen.tier);
        assertTrue(chosen.bppLaunch());
        assertTrue(chosen.compatibility());
    }

    @Test
    public void newerCodeWithoutAPreAdaptationWaits() {
        LaunchGate.Decision decision = decide(installed(12900, OTHER), RELEASE, false, true);
        assertEquals(Tier.NEWER_WAITING, decision.tier);
        assertFalse(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void theSameVersionCodeWithOtherCodeCountsAsNewer() {
        assertEquals(Tier.NEWER_COMPAT_AVAILABLE, decide(installed(12825, OTHER), RELEASE, true, false).tier);
        assertEquals(Tier.NEWER_WAITING, decide(installed(12825, OTHER), RELEASE, false, false).tier);
    }

    @Test
    public void olderCodeAsksForAGameUpdate() {
        LaunchGate.Decision decision = decide(installed(12800, OTHER), RELEASE, true, true);
        assertEquals(Tier.OLDER, decision.tier);
        assertFalse("a Pre-Adaptation never serves an older game", decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void notInstalledHasNoLaunchAtAll() {
        LaunchGate.Decision decision = decide(null, RELEASE, true, true);
        assertEquals(Tier.NOT_INSTALLED, decision.tier);
        assertFalse(decision.bppLaunch());
        assertFalse(decision.nativeLaunch());
    }

    @Test
    public void noManifestAndNoCacheIsNativeOnly() {
        LaunchGate.Decision decision = decide(installed(12825, ADAPTED), null, true, true);
        assertEquals(Tier.OFFLINE_NO_CACHE, decision.tier);
        assertFalse(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void theRetiredManifestShapeIsNoReleaseYet() {
        LaunchGate.Decision decision = LaunchGate.decide(
                new LaunchGate.Inputs(installed(12825, ADAPTED), null, true, false, false));
        assertEquals(Tier.NO_RELEASE, decision.tier);
        assertFalse(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }
}
