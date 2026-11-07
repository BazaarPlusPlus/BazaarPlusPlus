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
        return LaunchGate.decide(new LaunchGate.Inputs(installed, adapted, false));
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
                LaunchGate.decide(new LaunchGate.Inputs(copy, null, true)).tier);
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

    @Test
    public void theRetiredManifestShapeIsNoReleaseYet() {
        LaunchGate.Decision decision = LaunchGate.decide(
                new LaunchGate.Inputs(installed(12825, ADAPTED), null, true));
        assertEquals(Tier.NO_RELEASE, decision.tier);
        assertFalse(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }
}
