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
        return facts(decision, true, true);
    }

    static LauncherScreen.Facts facts(LaunchGate.Decision decision, boolean playInstalled, boolean installedFromPlay) {
        return new LauncherScreen.Facts(decision, "1.0.12825", "1.0.12825", false, false, false, false, null,
                playInstalled, installedFromPlay);
    }

    @Test
    public void theAdaptedGamePlaysWithTheOfficialGameBesideIt() {
        LauncherScreen screen = LauncherScreen.ready(facts(decision(installed(12825, true), LaunchGateTest.RELEASE,
                false)));
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals(Action.OPEN_OFFICIAL, screen.source);
        assertTrue(screen.notes.isEmpty());
        assertFalse(screen.busy);
    }

    @Test
    public void everyTierHasAStatusAndOneSourceRowAtMost() {
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
            for (boolean play : new boolean[] {true, false}) {
                LauncherScreen screen = LauncherScreen.ready(facts(decision, play, play));
                assertTrue(decision + " has a status", screen.status != null);
                assertFalse(decision + " repeats its primary", screen.source != null && screen.source == screen.primary);
                assertEquals(decision + " offers the official game exactly when it can open",
                        decision.nativeLaunch(), screen.source == Action.OPEN_OFFICIAL);
                assertEquals(decision + " offers Play exactly when the gate allows it",
                        decision.bppLaunch(), screen.primary == Action.PLAY);
                if (!play) {
                    assertFalse(decision + " sends nobody to a missing Play Store",
                            screen.primary == Action.INSTALL_FROM_PLAY || screen.primary == Action.UPDATE_FROM_PLAY);
                }
            }
        }
        assertEquals(EnumSet.allOf(Tier.class), covered);
    }

    @Test
    public void aNewerGameWaitsWithOnlyTheOfficialGame() {
        LauncherScreen waiting = LauncherScreen.ready(facts(decision(installed(12900, false), LaunchGateTest.RELEASE,
                false)));
        assertEquals(Status.WAITING, waiting.status);
        assertNull(waiting.primary);
        assertEquals(Action.OPEN_OFFICIAL, waiting.source);
        assertEquals(List.of(Note.GAME_NOT_ADAPTED), waiting.notes);
    }

    @Test
    public void installsComeFromPlayWhenItIsThereAndFromThePublisherOtherwise() {
        LaunchGate.Decision missing = decision(null, LaunchGateTest.RELEASE, false);
        LauncherScreen withPlay = LauncherScreen.ready(facts(missing, true, false));
        assertEquals(Status.NOT_INSTALLED, withPlay.status);
        assertEquals(Action.INSTALL_FROM_PLAY, withPlay.primary);
        assertEquals(Action.DOWNLOAD_FROM_PUBLISHER, withPlay.source);

        LauncherScreen withoutPlay = LauncherScreen.ready(facts(missing, false, false));
        assertEquals(Action.DOWNLOAD_FROM_PUBLISHER, withoutPlay.primary);
        assertNull("the publisher is already the primary", withoutPlay.source);

        LaunchGate.Decision replace = decision(new LaunchGate.Installed(12825, LaunchGateTest.ADAPTED, true),
                LaunchGateTest.RELEASE, false);
        assertEquals(Action.INSTALL_FROM_PLAY, LauncherScreen.ready(facts(replace, true, false)).primary);
        assertEquals(Action.DOWNLOAD_FROM_PUBLISHER, LauncherScreen.ready(facts(replace, false, false)).primary);
    }

    @Test
    public void anUpdateGoesBackToTheStoreThatInstalledTheGame() {
        LaunchGate.Decision older = decision(installed(12000, false), LaunchGateTest.RELEASE, false);
        LauncherScreen fromPlay = LauncherScreen.ready(facts(older, true, true));
        assertEquals(Status.GAME_UPDATE, fromPlay.status);
        assertEquals(Action.UPDATE_FROM_PLAY, fromPlay.primary);
        assertEquals(Action.OPEN_OFFICIAL, fromPlay.source);
        assertEquals(List.of(Note.UPDATE_GAME), fromPlay.notes);

        assertEquals("a sideloaded game updates from the publisher", Action.DOWNLOAD_FROM_PUBLISHER,
                LauncherScreen.ready(facts(older, true, false)).primary);
        assertEquals(Action.DOWNLOAD_FROM_PUBLISHER, LauncherScreen.ready(facts(older, false, true)).primary);
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
                update, true, true));
        assertEquals("the reminder takes nothing away", Action.PLAY, screen.primary);
        assertEquals("6.3.0", screen.appUpdate.version);
    }

    @Test
    public void aBuildWithoutRuntimeHidesPlayAndSaysWhy() {
        LaunchGate.Decision decision = decision(installed(12825, true), LaunchGateTest.RELEASE, false);
        LauncherScreen screen = LauncherScreen.ready(new LauncherScreen.Facts(decision, "1.0.12825", "1.0.12825", true,
                true, true, false, null, true, true));
        assertNull(screen.primary);
        assertEquals(List.of(Note.LAST_INSTALL_FAILED, Note.RUNTIME_MISSING, Note.FROM_CACHE), screen.notes);
    }

    @Test
    public void aDevelopmentInteropPlaysWithoutTheStagingNote() {
        LaunchGate.Decision decision = decision(installed(12900, false), null, false);
        LauncherScreen screen = LauncherScreen.ready(new LauncherScreen.Facts(decision, "1.0.12900", null, false, true,
                false, true, null, true, true));
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals(List.of(Note.DEV_INTEROP), screen.notes);
    }

    @Test
    public void busyScreensOfferNothingAndKeepTheStats() {
        LauncherScreen.Facts facts = facts(decision(installed(12825, true), LaunchGateTest.RELEASE, false));
        LauncherScreen downloading = LauncherScreen.launching(facts, new LauncherScreen.Progress(false, 25, 100));
        assertEquals(Status.DOWNLOADING, downloading.status);
        assertTrue(downloading.busy);
        assertNull(downloading.primary);
        assertEquals("the source row stays in place", Action.OPEN_OFFICIAL, downloading.source);
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
        assertEquals(Action.OPEN_OFFICIAL, check.source);
        assertFalse(check.failure.launch);
        assertNull("no official game to open", LauncherScreen.checkFailed("disk", false).source);

        LauncherScreen.Facts facts = facts(decision(installed(12825, true), LaunchGateTest.RELEASE, false));
        LauncherScreen launch = LauncherScreen.launchFailed(facts, "network");
        assertEquals(Status.READY, launch.status);
        assertEquals(Action.RETRY, launch.primary);
        assertEquals(Action.OPEN_OFFICIAL, launch.source);
        assertTrue(launch.failure.launch);
        assertEquals("network", launch.failure.message);
    }
}
