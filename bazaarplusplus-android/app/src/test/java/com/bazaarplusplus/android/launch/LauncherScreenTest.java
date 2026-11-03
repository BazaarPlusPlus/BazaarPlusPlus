package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.launch.LauncherScreen.Action;
import com.bazaarplusplus.android.launch.LauncherScreen.Note;
import com.bazaarplusplus.android.launch.LauncherScreen.Status;
import com.bazaarplusplus.android.launch.LaunchGate.Tier;

import org.junit.Test;

import java.util.EnumSet;
import java.util.List;

public class LauncherScreenTest {
    static LaunchGate.Decision decision(LaunchGate.Installed installed, LaunchGate.Adapted adapted, boolean noRelease) {
        return LaunchGate.decide(new LaunchGate.Inputs(installed, adapted, noRelease));
    }

    static LaunchGate.Installed installed(long versionCode, boolean adapted) {
        return new LaunchGate.Installed(versionCode, adapted ? LaunchGateTest.ADAPTED : LaunchGateTest.OTHER, false);
    }

    static LauncherScreen.Facts facts(LaunchGate.Decision decision) {
        return new LauncherScreen.Facts(decision, "1.0.12825", "1.0.12825", false, false, false, false, null);
    }

    @Test
    public void theAdaptedGamePlaysWithTheOfficialGameBesideIt() {
        LauncherScreen screen = LauncherScreen.ready(facts(decision(installed(12825, true), LaunchGateTest.RELEASE,
                false)));
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals(List.of(Action.OPEN_OFFICIAL), screen.secondary);
        assertTrue(screen.notes.isEmpty());
        assertFalse(screen.busy);
    }

    @Test
    public void everyTierHasAStatusAndAtMostTwoSecondaryActions() {
        LaunchGate.Decision[] decisions = {
            decision(null, null, false),
            decision(new LaunchGate.Installed(12825, LaunchGateTest.ADAPTED, true), LaunchGateTest.RELEASE, false),
            decision(installed(12825, true), null, false),
            decision(installed(12825, true), null, true),
            decision(installed(12825, true), LaunchGateTest.RELEASE, false),
            decision(installed(12900, false), LaunchGateTest.RELEASE, false),
            decision(installed(12000, false), LaunchGateTest.RELEASE, false),
        };
        EnumSet<Tier> covered = EnumSet.noneOf(Tier.class);
        for (LaunchGate.Decision decision : decisions) {
            covered.add(decision.tier);
            LauncherScreen screen = LauncherScreen.ready(facts(decision));
            assertTrue(decision + " has a status", screen.status != null);
            assertTrue(decision + " has at most two secondary actions", screen.secondary.size() <= 2);
            assertFalse(decision + " repeats its primary", screen.secondary.contains(screen.primary));
            assertEquals(decision + " offers the official game exactly when it can open",
                    decision.nativeLaunch(), screen.secondary.contains(Action.OPEN_OFFICIAL));
            assertEquals(decision + " offers Play exactly when the gate allows it",
                    decision.bppLaunch(), screen.primary == Action.PLAY);
        }
        assertEquals(EnumSet.allOf(Tier.class), covered);
    }

    @Test
    public void aNewerGameWaitsWithOnlyTheOfficialGame() {
        LauncherScreen waiting = LauncherScreen.ready(facts(decision(installed(12900, false), LaunchGateTest.RELEASE,
                false)));
        assertEquals(Status.WAITING, waiting.status);
        assertNull(waiting.primary);
        assertEquals(List.of(Action.OPEN_OFFICIAL), waiting.secondary);
    }

    @Test
    public void aNewerAppIsOnlyAReminderAndNeverForADevelopmentBuild() {
        assertNull(LauncherScreen.appUpdate(true, 6020000, 6020000, "6.2.0", null));
        assertNull(LauncherScreen.appUpdate(true, 6030000, 6020000, "6.2.0", null));
        assertEquals("6.3.0", LauncherScreen.appUpdate(true, 6020000, 6030000, "6.3.0", null).version);
        assertNull(LauncherScreen.appUpdate(false, 1, 6030000, "6.3.0", null));

        LauncherScreen.AppUpdate update = LauncherScreen.appUpdate(true, 6020000, 6030000, "6.3.0", "https://x");
        LauncherScreen screen = LauncherScreen.ready(new LauncherScreen.Facts(decision(installed(12825, true),
                LaunchGateTest.RELEASE, false), "1.0.12825", "1.0.12825", false, false, false, false,
                update));
        assertEquals("the reminder takes nothing away", Action.PLAY, screen.primary);
        assertEquals("6.3.0", screen.appUpdate.version);
    }

    @Test
    public void aBuildWithoutRuntimeHidesPlayAndSaysWhy() {
        LaunchGate.Decision decision = decision(installed(12825, true), LaunchGateTest.RELEASE, false);
        LauncherScreen screen = LauncherScreen.ready(new LauncherScreen.Facts(decision, "1.0.12825", "1.0.12825", true,
                true, true, false, null));
        assertNull(screen.primary);
        assertEquals(List.of(Note.LAST_INSTALL_FAILED, Note.RUNTIME_MISSING, Note.FROM_CACHE), screen.notes);
    }

    @Test
    public void aDevelopmentInteropPlaysWithoutTheStagingNote() {
        LaunchGate.Decision decision = decision(installed(12900, false), null, false);
        LauncherScreen screen = LauncherScreen.ready(new LauncherScreen.Facts(decision, "1.0.12900", null, false, true,
                false, true, null));
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals(List.of(Note.DEV_INTEROP), screen.notes);
    }

    @Test
    public void busyScreensOfferNothingAndKeepTheInfoRows() {
        LauncherScreen.Facts facts = facts(decision(installed(12825, true), LaunchGateTest.RELEASE, false));
        LauncherScreen downloading = LauncherScreen.launching(facts, new LauncherScreen.Progress(false, 25, 100));
        assertEquals(Status.DOWNLOADING, downloading.status);
        assertTrue(downloading.busy);
        assertNull(downloading.primary);
        assertTrue(downloading.secondary.isEmpty());
        assertEquals(25, downloading.progress.percent());
        assertEquals("1.0.12825", downloading.gameVersion);

        assertEquals(Status.PREPARING, LauncherScreen.launching(facts, null).status);
        LauncherScreen checking = LauncherScreen.checking(facts);
        assertEquals(Status.CHECKING, checking.status);
        assertEquals("1.0.12825", checking.adaptedVersion);
        assertTrue(LauncherScreen.checking(null).busy);
    }

    @Test
    public void failuresOfferRetry() {
        LauncherScreen check = LauncherScreen.checkFailed("disk", true);
        assertNull(check.status);
        assertEquals(Action.RETRY, check.primary);
        assertEquals(List.of(Action.OPEN_OFFICIAL), check.secondary);
        assertFalse(check.failure.launch);
        assertTrue("no official game to open", LauncherScreen.checkFailed("disk", false).secondary.isEmpty());

        LauncherScreen.Facts facts = facts(decision(installed(12825, true), LaunchGateTest.RELEASE, false));
        LauncherScreen launch = LauncherScreen.launchFailed(facts, "network");
        assertEquals(Status.READY, launch.status);
        assertEquals(Action.RETRY, launch.primary);
        assertEquals(List.of(Action.OPEN_OFFICIAL), launch.secondary);
        assertTrue(launch.failure.launch);
        assertEquals("network", launch.failure.message);
    }
}
