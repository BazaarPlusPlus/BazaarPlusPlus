package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.launch.LaunchGate.Tier;

import org.junit.Test;

import java.util.List;
import java.util.Set;

public class LaunchGateTest {
    /** The publisher's adapted game. */
    static final GameCodeHash ADAPTED = new GameCodeHash("a".repeat(64), "b".repeat(64));
    /** Google Play's adapted game. */
    static final GameCodeHash PLAY_ADAPTED = new GameCodeHash("e".repeat(64), "f".repeat(64));
    static final GameCodeHash OTHER = new GameCodeHash("c".repeat(64), "d".repeat(64));
    static final String PUBLISHER_SIGNER = "f8f508cf8b590e715a2f5bdba78098f03daf988cf828dd61fcbee8aa9b21632a";
    static final String PLAY_SIGNER = "f093884efa76e15b6375faa0c1ef195f4f5c411719e568e043fd82103c399b72";
    static final LaunchGate.Adapted PUBLISHER = new LaunchGate.Adapted("publisher", PUBLISHER_SIGNER, 12825, ADAPTED);
    static final LaunchGate.Adapted PLAY = new LaunchGate.Adapted("play", PLAY_SIGNER, 13168, PLAY_ADAPTED);
    static final List<LaunchGate.Adapted> RELEASE = List.of(PUBLISHER, PLAY);

    static LaunchGate.Decision decide(LaunchGate.Installed installed, List<LaunchGate.Adapted> adaptations) {
        return decide(installed, adaptations, false);
    }

    static LaunchGate.Decision decide(LaunchGate.Installed installed, List<LaunchGate.Adapted> adaptations,
            boolean developmentInterop) {
        LaunchGate.Decision decision = LaunchGate.decide(new LaunchGate.Inputs(installed, adaptations,
                developmentInterop));
        assertEquals(developmentInterop, decision.developmentInterop);
        return decision;
    }

    /**
     * {@code bppLaunch()} with and without a Development interop, and the tier either way: the
     * Development interop is a fact for the BPP launch rule, never for the tier.
     */
    static void assertBppLaunch(LaunchGate.Installed installed, List<LaunchGate.Adapted> adapted, Tier tier,
            boolean without, boolean with) {
        LaunchGate.Decision plain = decide(installed, adapted, false);
        LaunchGate.Decision development = decide(installed, adapted, true);
        assertEquals(tier, plain.tier);
        assertEquals(tier, development.tier);
        assertEquals(tier + " without a Development interop", without, plain.bppLaunch());
        assertEquals(tier + " with a Development interop", with, development.bppLaunch());
        assertEquals("native launch ignores the Development interop", plain.nativeLaunch(), development.nativeLaunch());
    }

    /** A game the publisher signed. */
    static LaunchGate.Installed installed(long versionCode, GameCodeHash hash) {
        return installed(versionCode, hash, Set.of(PUBLISHER_SIGNER));
    }

    static LaunchGate.Installed installed(long versionCode, GameCodeHash hash, Set<String> signers) {
        return new LaunchGate.Installed(versionCode, hash, signers, false);
    }

    @Test
    public void anAndroidReleaseApkGetsNoLaunchEvenWithTheAdaptedCode() {
        LaunchGate.Installed copy = new LaunchGate.Installed(12825, ADAPTED, Set.of(PUBLISHER_SIGNER), true);
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
        LaunchGate.Installed copy = new LaunchGate.Installed(12825, ADAPTED, Set.of(PUBLISHER_SIGNER), true);
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
    public void aDevelopmentInteropLaunchesAGameOfNoSource() {
        assertBppLaunch(installed(12000, OTHER, Set.of("9".repeat(64))), RELEASE, Tier.NEWER_WAITING, false, true);
    }

    @Test
    public void matchingCodeIsNormal() {
        LaunchGate.Decision decision = decide(installed(12825, ADAPTED), RELEASE);
        assertEquals(Tier.NORMAL, decision.tier);
        assertSame(PUBLISHER, decision.adaptation);
        assertTrue(decision.bppLaunch());
        assertTrue(decision.nativeLaunch());
    }

    @Test
    public void theCodeOfAnyAdaptationSelectsIt() {
        LaunchGate.Decision decision = decide(installed(13168, PLAY_ADAPTED, Set.of(PLAY_SIGNER)), RELEASE);
        assertEquals(Tier.NORMAL, decision.tier);
        assertSame(PLAY, decision.adaptation);
        assertEquals("NORMAL (play)", decision.toString());
    }

    @Test
    public void matchingCodeWinsWhenTheSignersCannotBeRead() {
        LaunchGate.Decision decision = decide(installed(13168, PLAY_ADAPTED, Set.of()), RELEASE);
        assertEquals(Tier.NORMAL, decision.tier);
        assertSame(PLAY, decision.adaptation);
        assertTrue(decision.bppLaunch());
    }

    @Test
    public void matchingCodeWinsOverAnotherSourcesSigner() {
        assertSame(PLAY, decide(installed(13168, PLAY_ADAPTED, Set.of(PUBLISHER_SIGNER)), RELEASE).adaptation);
    }

    @Test
    public void anOlderPlayGameComparesWithPlayOnly() {
        // 13100 is newer than the publisher's 12825: only Play's 13168 counts for a Play game.
        LaunchGate.Decision decision = decide(installed(13100, OTHER, Set.of(PLAY_SIGNER)), RELEASE);
        assertEquals(Tier.OLDER, decision.tier);
        assertSame(PLAY, decision.adaptation);
        assertFalse(decision.bppLaunch());
    }

    @Test
    public void aPublisherGameIsNotOlderThanPlaysAdaptation() {
        LaunchGate.Decision decision = decide(installed(13000, OTHER), RELEASE);
        assertEquals(Tier.NEWER_WAITING, decision.tier);
        assertSame(PUBLISHER, decision.adaptation);
    }

    @Test
    public void aGameOfNoSourceWaitsWhateverItsVersionCode() {
        for (long versionCode : new long[] {12000, 13168, 14000}) {
            LaunchGate.Decision decision = decide(installed(versionCode, OTHER, Set.of("9".repeat(64))), RELEASE);
            assertEquals(Tier.NEWER_WAITING, decision.tier);
            assertNull(decision.adaptation);
            assertFalse(decision.bppLaunch());
            assertTrue(decision.nativeLaunch());
        }
        assertNull("no signers, no source", decide(installed(12000, OTHER, Set.of()), RELEASE).adaptation);
    }

    @Test
    public void onlyTheTiersThatCompareSelectAnAdaptation() {
        assertNull(decide(null, RELEASE).adaptation);
        assertNull(decide(installed(12825, ADAPTED), null).adaptation);
        assertNull(decide(new LaunchGate.Installed(12825, ADAPTED, Set.of(PUBLISHER_SIGNER), true), RELEASE).adaptation);
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
        assertSame(PUBLISHER, decision.adaptation);
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
        assertSame(PUBLISHER, decision.adaptation);
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
