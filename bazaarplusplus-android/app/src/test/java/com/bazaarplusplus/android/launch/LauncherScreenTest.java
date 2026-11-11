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
    static LaunchGate.Decision decision(LaunchGate.Installed installed, LaunchGate.Adapted adapted) {
        return LaunchGate.decide(new LaunchGate.Inputs(installed, adapted));
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

    /** No primary action. */
    static final Action NONE = null;

    /**
     * One row of the primary-action matrix: a Development interop or none, a tier, and the
     * primary for each of the four combinations of the game installed or not (a versionName or
     * null in the facts) and the runtime present or missing.
     */
    static final class Row {
        final boolean devInterop;
        final Tier tier;
        final Action[] primaries;

        Row(boolean devInterop, Tier tier, Action installedWithRuntime, Action installedNoRuntime,
                Action notInstalledWithRuntime, Action notInstalledNoRuntime) {
            this.devInterop = devInterop;
            this.tier = tier;
            this.primaries = new Action[] {
                installedWithRuntime, installedNoRuntime, notInstalledWithRuntime, notInstalledNoRuntime};
        }
    }

    /**
     * The primary action as today's {@link LauncherScreen#ready} decides it, with Google Play
     * installed and the game installed from it. The facts are independent of the tier, so the
     * table holds cells that {@code LaunchCheck.run} never builds: it gives a game that is not
     * installed the tier NOT_INSTALLED and no Development interop.
     */
    static final Row[] PRIMARY = {
        //                                       installed                                           not installed
        //                                       runtime                   no runtime                runtime                   no runtime
        new Row(false, Tier.NOT_INSTALLED,       Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY),
        new Row(false, Tier.ANDROID_RELEASE_APK, Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY),
        new Row(false, Tier.OFFLINE_NO_CACHE,    Action.RETRY,             Action.RETRY,             Action.RETRY,             Action.RETRY),
        new Row(false, Tier.NORMAL,              Action.PLAY,              NONE,                     Action.PLAY,              NONE),
        new Row(false, Tier.NEWER_WAITING,       NONE,                     NONE,                     NONE,                     NONE),
        new Row(false, Tier.OLDER,               Action.UPDATE_FROM_PLAY,  Action.UPDATE_FROM_PLAY,  Action.UPDATE_FROM_PLAY,  Action.UPDATE_FROM_PLAY),
        // A Development interop plays whenever the game is installed, before the tier decides.
        new Row(true,  Tier.NOT_INSTALLED,       Action.PLAY,              NONE,                     Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY),
        new Row(true,  Tier.ANDROID_RELEASE_APK, Action.PLAY,              NONE,                     Action.INSTALL_FROM_PLAY, Action.INSTALL_FROM_PLAY),
        new Row(true,  Tier.OFFLINE_NO_CACHE,    Action.PLAY,              NONE,                     Action.RETRY,             Action.RETRY),
        new Row(true,  Tier.NORMAL,              Action.PLAY,              NONE,                     Action.PLAY,              NONE),
        new Row(true,  Tier.NEWER_WAITING,       Action.PLAY,              NONE,                     NONE,                     NONE),
        new Row(true,  Tier.OLDER,               Action.PLAY,              NONE,                     Action.UPDATE_FROM_PLAY,  Action.UPDATE_FROM_PLAY),
    };

    /** A decision of {@code tier}, through the gate. */
    static LaunchGate.Decision decision(Tier tier) {
        LaunchGate.Decision decision;
        switch (tier) {
            case NOT_INSTALLED: decision = decision(null, LaunchGateTest.RELEASE); break;
            case ANDROID_RELEASE_APK:
                decision = decision(new LaunchGate.Installed(12825, LaunchGateTest.ADAPTED, true), LaunchGateTest.RELEASE);
                break;
            case OFFLINE_NO_CACHE: decision = decision(installed(12825, true), null); break;
            case NORMAL: decision = decision(installed(12825, true), LaunchGateTest.RELEASE); break;
            case NEWER_WAITING: decision = decision(installed(12900, false), LaunchGateTest.RELEASE); break;
            case OLDER: decision = decision(installed(12000, false), LaunchGateTest.RELEASE); break;
            default: throw new AssertionError(tier);
        }
        assertEquals(tier, decision.tier);
        return decision;
    }

    static LauncherScreen.Facts facts(Tier tier, boolean devInterop, boolean gameInstalled, boolean runtimeMissing) {
        return new LauncherScreen.Facts(decision(tier), gameInstalled ? "1.0.12825" : null, "1.0.12825", false, false,
                runtimeMissing, devInterop, null, true, true);
    }

    @Test
    public void thePrimaryActionMatrix() {
        int cells = 0;
        for (boolean devInterop : new boolean[] {false, true}) {
            EnumSet<Tier> covered = EnumSet.noneOf(Tier.class);
            for (Row row : PRIMARY) {
                if (row.devInterop != devInterop) continue;
                assertTrue(row.tier + " appears once", covered.add(row.tier));
                int column = 0;
                for (boolean gameInstalled : new boolean[] {true, false}) {
                    for (boolean runtimeMissing : new boolean[] {false, true}) {
                        LauncherScreen screen = LauncherScreen.ready(facts(row.tier, devInterop, gameInstalled, runtimeMissing));
                        assertEquals("devInterop=" + devInterop + " " + row.tier + " installed=" + gameInstalled
                                + " runtimeMissing=" + runtimeMissing, row.primaries[column], screen.primary);
                        column++;
                        cells++;
                    }
                }
            }
            assertEquals("every tier has a row, devInterop=" + devInterop, EnumSet.allOf(Tier.class), covered);
        }
        assertEquals(2 * Tier.values().length * 2 * 2, cells);
    }

    @Test
    public void aDevelopmentInteropPlaysInTheReplaceTierWhereTheGateRefuses() {
        LauncherScreen.Facts facts = facts(Tier.ANDROID_RELEASE_APK, true, true, false);
        assertFalse(facts.decision.bppLaunch());
        LauncherScreen screen = LauncherScreen.ready(facts);
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals("the replace note is gone", List.of(Note.DEV_INTEROP), screen.notes);
        assertEquals("the official game cannot open, so the publisher is the source row",
                Action.DOWNLOAD_FROM_PUBLISHER, screen.source);
    }

    @Test
    public void aDevelopmentInteropPlaysOfflineWhereTheGateRefuses() {
        LauncherScreen.Facts facts = facts(Tier.OFFLINE_NO_CACHE, true, true, false);
        assertFalse(facts.decision.bppLaunch());
        LauncherScreen screen = LauncherScreen.ready(facts);
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals("the release-unavailable note is gone", List.of(Note.DEV_INTEROP), screen.notes);
        assertEquals(Action.OPEN_OFFICIAL, screen.source);
    }

    @Test
    public void theAdaptedGamePlaysWithTheOfficialGameBesideIt() {
        LauncherScreen screen = LauncherScreen.ready(facts(decision(installed(12825, true), LaunchGateTest.RELEASE)));
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals(Action.OPEN_OFFICIAL, screen.source);
        assertTrue(screen.notes.isEmpty());
        assertFalse(screen.busy);
    }

    @Test
    public void everyTierHasAStatusAndOneSourceRowAtMost() {
        LaunchGate.Decision[] decisions = {
            decision(null, null),
            decision(new LaunchGate.Installed(12825, LaunchGateTest.ADAPTED, true), LaunchGateTest.RELEASE),
            decision(installed(12825, true), null),
            decision(installed(12825, true), LaunchGateTest.RELEASE),
            decision(installed(12900, false), LaunchGateTest.RELEASE),
            decision(installed(12000, false), LaunchGateTest.RELEASE),
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
        LauncherScreen waiting = LauncherScreen.ready(facts(decision(installed(12900, false), LaunchGateTest.RELEASE)));
        assertEquals(Status.WAITING, waiting.status);
        assertNull(waiting.primary);
        assertEquals(Action.OPEN_OFFICIAL, waiting.source);
        assertEquals(List.of(Note.GAME_NOT_ADAPTED), waiting.notes);
    }

    @Test
    public void installsComeFromPlayWhenItIsThereAndFromThePublisherOtherwise() {
        LaunchGate.Decision missing = decision(null, LaunchGateTest.RELEASE);
        LauncherScreen withPlay = LauncherScreen.ready(facts(missing, true, false));
        assertEquals(Status.NOT_INSTALLED, withPlay.status);
        assertEquals(Action.INSTALL_FROM_PLAY, withPlay.primary);
        assertEquals(Action.DOWNLOAD_FROM_PUBLISHER, withPlay.source);

        LauncherScreen withoutPlay = LauncherScreen.ready(facts(missing, false, false));
        assertEquals(Action.DOWNLOAD_FROM_PUBLISHER, withoutPlay.primary);
        assertNull("the publisher is already the primary", withoutPlay.source);

        LaunchGate.Decision replace = decision(new LaunchGate.Installed(12825, LaunchGateTest.ADAPTED, true),
                LaunchGateTest.RELEASE);
        assertEquals(Action.INSTALL_FROM_PLAY, LauncherScreen.ready(facts(replace, true, false)).primary);
        assertEquals(Action.DOWNLOAD_FROM_PUBLISHER, LauncherScreen.ready(facts(replace, false, false)).primary);
    }

    @Test
    public void anUpdateGoesBackToTheStoreThatInstalledTheGame() {
        LaunchGate.Decision older = decision(installed(12000, false), LaunchGateTest.RELEASE);
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
                LaunchGateTest.RELEASE), "1.0.12825", "1.0.12825", false, false, false, false,
                update, true, true));
        assertEquals("the reminder takes nothing away", Action.PLAY, screen.primary);
        assertEquals("6.3.0", screen.appUpdate.version);
    }

    @Test
    public void aBuildWithoutRuntimeHidesPlayAndSaysWhy() {
        LaunchGate.Decision decision = decision(installed(12825, true), LaunchGateTest.RELEASE);
        LauncherScreen screen = LauncherScreen.ready(new LauncherScreen.Facts(decision, "1.0.12825", "1.0.12825", true,
                true, true, false, null, true, true));
        assertNull(screen.primary);
        assertEquals(List.of(Note.LAST_INSTALL_FAILED, Note.RUNTIME_MISSING, Note.FROM_CACHE), screen.notes);
    }

    @Test
    public void aDevelopmentInteropPlaysWithoutTheStagingNote() {
        LaunchGate.Decision decision = decision(installed(12900, false), null);
        LauncherScreen screen = LauncherScreen.ready(new LauncherScreen.Facts(decision, "1.0.12900", null, false, true,
                false, true, null, true, true));
        assertEquals(Status.READY, screen.status);
        assertEquals(Action.PLAY, screen.primary);
        assertEquals(List.of(Note.DEV_INTEROP), screen.notes);
    }

    @Test
    public void busyScreensOfferNothingAndKeepTheStats() {
        LauncherScreen.Facts facts = facts(decision(installed(12825, true), LaunchGateTest.RELEASE));
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

        LauncherScreen.Facts facts = facts(decision(installed(12825, true), LaunchGateTest.RELEASE));
        LauncherScreen launch = LauncherScreen.launchFailed(facts, "network");
        assertEquals(Status.READY, launch.status);
        assertEquals(Action.RETRY, launch.primary);
        assertEquals(Action.OPEN_OFFICIAL, launch.source);
        assertTrue(launch.failure.launch);
        assertEquals("network", launch.failure.message);
    }
}
