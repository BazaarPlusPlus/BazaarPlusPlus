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

    static LaunchGate.Decision decide(LaunchGate.Installed installed, LaunchGate.Adapted adapted) {
        return decide(installed, adapted, false);
    }

    static LaunchGate.Decision decide(LaunchGate.Installed installed, LaunchGate.Adapted adapted,
            boolean developmentInterop) {
        LaunchGate.Decision decision = LaunchGate.decide(new LaunchGate.Inputs(installed, adapted, developmentInterop));
        assertEquals(developmentInterop, decision.developmentInterop);
        return decision;
    }

    /**
     * {@code bppLaunch()} with and without a Development interop, and the tier either way: the
     * Development interop is a fact for the BPP launch rule, never for the tier.
     */
    static void assertBppLaunch(LaunchGate.Installed installed, LaunchGate.Adapted adapted, Tier tier,
            boolean without, boolean with) {
        LaunchGate.Decision plain = decide(installed, adapted, false);
        LaunchGate.Decision development = decide(installed, adapted, true);
        assertEquals(tier, plain.tier);
        assertEquals(tier, development.tier);
        assertEquals(tier + " without a Development interop", without, plain.bppLaunch());
        assertEquals(tier + " with a Development interop", with, development.bppLaunch());
        assertEquals("native launch ignores the Development interop", plain.nativeLaunch(), development.nativeLaunch());
    }

    static LaunchGate.Installed installed(long versionCode, GameCodeHash hash) {
        return new LaunchGate.Installed(versionCode, hash, false);
    }

    @Test
    public void anAndroidReleaseApkGetsNoLaunchEvenWithTheAdaptedCode() {
        LaunchGate.Installed copy = new LaunchGate.Installed(12825, ADAPTED, true);
        LaunchGate.Decision decision = decide(copy, RELEASE);
        assertEquals(Tier.ANDROID_RELEASE_APK, decision.tier);
        assertFalse(decision.bppLaunch());
        assertFalse(decision.nativeLaunch());
        assertEquals(Tier.ANDROID_RELEASE_APK,
                LaunchGate.decide(new LaunchGate.Inputs(copy, null, false)).tier);
    }

    @Test
    public void aDevelopmentInteropNeverLaunchesAnAndroidReleaseApk() {
        // LemonLoader would deploy the copy's assets/LemonLoader/deployment/ over the Mods (ADR 0012).
        LaunchGate.Installed copy = new LaunchGate.Installed(12825, ADAPTED, true);
        assertBppLaunch(copy, RELEASE, Tier.ANDROID_RELEASE_APK, false, false);
        assertBppLaunch(copy, null, Tier.ANDROID_RELEASE_APK, false, false);
    }

    @Test
    public void theAdaptedGameLaunchesWithOrWithoutADevelopmentInterop() {
        assertBppLaunch(installed(12825, ADAPTED), RELEASE, Tier.NORMAL, true, true);
    }

    @Test
    public void aDevelopmentInteropLaunchesANewerGame() {
        assertBppLaunch(installed(12900, OTHER), RELEASE, Tier.NEWER_WAITING, false, true);
    }

    @Test
    public void aDevelopmentInteropLaunchesAnOlderGame() {
        assertBppLaunch(installed(12800, OTHER), RELEASE, Tier.OLDER, false, true);
    }

    @Test
    public void aDevelopmentInteropLaunchesWithoutAManifest() {
        assertBppLaunch(installed(12825, ADAPTED), null, Tier.OFFLINE_NO_CACHE, false, true);
    }

    @Test
    public void aDevelopmentInteropCannotLaunchAMissingGame() {
        assertBppLaunch(null, RELEASE, Tier.NOT_INSTALLED, false, false);
        assertBppLaunch(null, null, Tier.NOT_INSTALLED, false, false);
    }

    @Test
    public void matchingCodeIsNormal() {
        LaunchGate.Decision decision = decide(installed(12825, ADAPTED), RELEASE);
        assertEquals(Tier.NORMAL, decision.tier);
        assertTrue(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void aContentOnlyUpdateWithTheSameCodeStaysNormal() {
        // Only the Game Code Hash decides: a higher versionCode with the adapted code is normal.
        assertEquals(Tier.NORMAL, decide(installed(12900, ADAPTED), RELEASE).tier);
    }

    @Test
    public void newerCodeWaits() {
        LaunchGate.Decision decision = decide(installed(12900, OTHER), RELEASE);
        assertEquals(Tier.NEWER_WAITING, decision.tier);
        assertFalse(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void theSameVersionCodeWithOtherCodeCountsAsNewer() {
        assertEquals(Tier.NEWER_WAITING, decide(installed(12825, OTHER), RELEASE).tier);
    }

    @Test
    public void olderCodeAsksForAGameUpdate() {
        LaunchGate.Decision decision = decide(installed(12800, OTHER), RELEASE);
        assertEquals(Tier.OLDER, decision.tier);
        assertFalse(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void notInstalledHasNoLaunchAtAll() {
        LaunchGate.Decision decision = decide(null, RELEASE);
        assertEquals(Tier.NOT_INSTALLED, decision.tier);
        assertFalse(decision.bppLaunch());
        assertFalse(decision.nativeLaunch());
    }

    @Test
    public void noManifestAndNoCacheIsNativeOnly() {
        LaunchGate.Decision decision = decide(installed(12825, ADAPTED), null);
        assertEquals(Tier.OFFLINE_NO_CACHE, decision.tier);
        assertFalse(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }
}
