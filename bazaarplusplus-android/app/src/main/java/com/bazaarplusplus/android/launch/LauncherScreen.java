package com.bazaarplusplus.android.launch;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * What the launcher shows, as plain data (design/components/Launcher): the status badge, the one
 * primary action, up to two secondary actions, notes, banners, download progress and the info
 * rows. {@link LauncherActivity} renders it into whichever layout the window width picks, so a
 * fold re-renders the same screen without running anything again.
 */
final class LauncherScreen {
    /** The badge word and its tone; the same words as the iOS App. */
    enum Status {
        CHECKING(Tone.BUSY), PREPARING(Tone.BUSY), DOWNLOADING(Tone.BUSY),
        READY(Tone.OK), WAITING(Tone.WARN), GAME_UPDATE(Tone.WARN),
        NOT_INSTALLED(Tone.NEUTRAL), REPLACE(Tone.BAD), OFFLINE(Tone.NEUTRAL),
        NOT_RELEASED(Tone.NEUTRAL);

        final Tone tone;

        Status(Tone tone) {
            this.tone = tone;
        }
    }

    enum Tone { OK, WARN, BAD, BUSY, NEUTRAL }

    enum Action { PLAY, INSTALL_FROM_PLAY, UPDATE_FROM_PLAY, DOWNLOAD_FROM_PUBLISHER, OPEN_OFFICIAL, RETRY }

    enum Note { FROM_CACHE, LAST_INSTALL_FAILED, RUNTIME_MISSING, REPLACE_GAME, RELEASE_UNAVAILABLE, NO_RELEASE, DEV_INTEROP }

    /** A failure that stopped the check or the launch; shown as an error banner over a Retry. */
    static final class Failure {
        final boolean launch;
        final String message;

        Failure(boolean launch, String message) {
            this.launch = launch;
            this.message = message;
        }
    }

    /** A newer App: a reminder banner that takes no action away. */
    static final class AppUpdate {
        final String version;
        /** Null when the release names no download. */
        final String url;

        AppUpdate(String version, String url) {
            this.version = version;
            this.url = url;
        }
    }

    /** A running download with a known size. */
    static final class Progress {
        final boolean interop;
        final long received;
        final long total;

        Progress(boolean interop, long received, long total) {
            this.interop = interop;
            this.received = received;
            this.total = total;
        }

        int percent() {
            return total <= 0 ? 0 : (int) Math.min(100, received * 100 / total);
        }
    }

    /** The facts of one launch check that the screen depends on. */
    static final class Facts {
        final LaunchGate.Decision decision;
        /** Installed game versionName, or null when not installed. */
        final String gameVersion;
        /** The release's adapted game version, or null without a release. */
        final String adaptedVersion;
        final boolean fromCache;
        final boolean lastInstallFailed;
        final boolean runtimeMissing;
        final boolean devInterop;
        /** Null unless the release names a newer App than this one. */
        final AppUpdate appUpdate;

        Facts(LaunchGate.Decision decision, String gameVersion, String adaptedVersion, boolean fromCache,
                boolean lastInstallFailed, boolean runtimeMissing, boolean devInterop, AppUpdate appUpdate) {
            this.decision = decision;
            this.gameVersion = gameVersion;
            this.adaptedVersion = adaptedVersion;
            this.fromCache = fromCache;
            this.lastInstallFailed = lastInstallFailed;
            this.runtimeMissing = runtimeMissing;
            this.devInterop = devInterop;
            this.appUpdate = appUpdate;
        }
    }

    /** Null while a check runs or after it failed. */
    final Status status;
    /** Null when there is nothing to do next. */
    final Action primary;
    final List<Action> secondary;
    final List<Note> notes;
    final AppUpdate appUpdate;
    final Failure failure;
    final Progress progress;
    final String gameVersion;
    final String adaptedVersion;
    /** No action runs while a check or a launch is in progress. */
    final boolean busy;

    private LauncherScreen(Status status, Action primary, List<Action> secondary, List<Note> notes, AppUpdate appUpdate,
            Failure failure, Progress progress, String gameVersion, String adaptedVersion, boolean busy) {
        this.status = status;
        this.primary = primary;
        this.secondary = Collections.unmodifiableList(secondary);
        this.notes = Collections.unmodifiableList(notes);
        this.appUpdate = appUpdate;
        this.failure = failure;
        this.progress = progress;
        this.gameVersion = gameVersion;
        this.adaptedVersion = adaptedVersion;
        this.busy = busy;
    }

    /** A check is running; the last known facts keep the banner and the info rows in place. */
    static LauncherScreen checking(Facts last) {
        return new LauncherScreen(Status.CHECKING, null, List.of(), List.of(), last == null ? null : last.appUpdate, null,
                null, last == null ? null : last.gameVersion, last == null ? null : last.adaptedVersion, true);
    }

    /** The check itself failed: no status to show, only Retry and, when it is installed, the official game. */
    static LauncherScreen checkFailed(String message, boolean officialInstalled) {
        return new LauncherScreen(null, Action.RETRY, officialInstalled ? List.of(Action.OPEN_OFFICIAL) : List.of(),
                List.of(), null, new Failure(false, message), null, null, null, false);
    }

    /** A BPP launch is preparing the game; {@code progress} is null between downloads. */
    static LauncherScreen launching(Facts facts, Progress progress) {
        return new LauncherScreen(progress == null ? Status.PREPARING : Status.DOWNLOADING, null, List.of(), List.of(),
                facts.appUpdate, null, progress, facts.gameVersion, facts.adaptedVersion, true);
    }

    /** The BPP launch failed: the check's screen with an error banner and Retry. */
    static LauncherScreen launchFailed(Facts facts, String message) {
        LauncherScreen ready = ready(facts);
        return new LauncherScreen(ready.status, Action.RETRY, ready.secondary, ready.notes, ready.appUpdate,
                new Failure(true, message), null, ready.gameVersion, ready.adaptedVersion, false);
    }

    /** What the tier allows (ADR 0012, "三档启动规则"). */
    static LauncherScreen ready(Facts facts) {
        LaunchGate.Decision decision = facts.decision;
        List<Action> secondary = new ArrayList<>();
        List<Note> notes = new ArrayList<>();
        Status status;
        Action primary = null;
        AppUpdate update = facts.appUpdate;

        if (facts.devInterop && facts.gameVersion != null) {
            status = Status.READY;
            notes.add(Note.DEV_INTEROP);
            primary = play(facts, notes);
        } else {
            switch (decision.tier) {
                case NOT_INSTALLED:
                    status = Status.NOT_INSTALLED;
                    primary = Action.INSTALL_FROM_PLAY;
                    secondary.add(Action.DOWNLOAD_FROM_PUBLISHER);
                    break;
                case ANDROID_RELEASE_APK:
                    status = Status.REPLACE;
                    notes.add(Note.REPLACE_GAME);
                    primary = Action.INSTALL_FROM_PLAY;
                    secondary.add(Action.DOWNLOAD_FROM_PUBLISHER);
                    break;
                case OFFLINE_NO_CACHE:
                    status = Status.OFFLINE;
                    notes.add(Note.RELEASE_UNAVAILABLE);
                    primary = Action.RETRY;
                    break;
                case NO_RELEASE:
                    status = Status.NOT_RELEASED;
                    notes.add(Note.NO_RELEASE);
                    break;
                case OLDER:
                    status = Status.GAME_UPDATE;
                    primary = Action.UPDATE_FROM_PLAY;
                    secondary.add(Action.DOWNLOAD_FROM_PUBLISHER);
                    break;
                case NEWER_WAITING:
                    status = Status.WAITING;
                    break;
                case NORMAL:
                default:
                    status = Status.READY;
                    primary = play(facts, notes);
            }
        }
        if (decision.nativeLaunch()) secondary.add(Action.OPEN_OFFICIAL);
        if (facts.fromCache) notes.add(Note.FROM_CACHE);
        return new LauncherScreen(status, primary, secondary, notes, update, null, null, facts.gameVersion,
                facts.adaptedVersion, false);
    }

    /**
     * The reminder for a newer App, or null. A release build is reminded when the release's App
     * has a higher versionCode; a development build (no runtime digest) never is, since every
     * release outnumbers its versionCode.
     */
    static AppUpdate appUpdate(boolean releaseBuild, long ownVersionCode, long releaseVersionCode,
            String releaseVersion, String url) {
        if (!releaseBuild || releaseVersionCode <= ownVersionCode) return null;
        return new AppUpdate(releaseVersion, url);
    }

    /** Play, or nothing when this build carries no runtime to play with. */
    private static Action play(Facts facts, List<Note> notes) {
        // A Development interop stages nothing, so the staging note does not apply to it.
        if (facts.lastInstallFailed && !facts.devInterop) notes.add(Note.LAST_INSTALL_FAILED);
        if (facts.runtimeMissing) {
            notes.add(Note.RUNTIME_MISSING);
            return null;
        }
        return Action.PLAY;
    }
}
