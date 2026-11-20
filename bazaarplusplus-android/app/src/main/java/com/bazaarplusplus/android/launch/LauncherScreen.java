package com.bazaarplusplus.android.launch;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

final class LauncherScreen {
    enum Status {
        CHECKING(Tone.BUSY), PREPARING(Tone.BUSY), DOWNLOADING(Tone.BUSY),
        READY(Tone.OK), WAITING(Tone.WARN), GAME_UPDATE(Tone.WARN),
        NOT_INSTALLED(Tone.NEUTRAL), REPLACE(Tone.BAD), OFFLINE(Tone.NEUTRAL);

        final Tone tone;

        Status(Tone tone) {
            this.tone = tone;
        }
    }

    enum Tone { OK, WARN, BAD, BUSY, NEUTRAL }

    enum Action { PLAY, INSTALL_FROM_PLAY, UPDATE_FROM_PLAY, DOWNLOAD_FROM_PUBLISHER, OPEN_OFFICIAL, RETRY }

    enum Note {
        FROM_CACHE, LAST_INSTALL_FAILED, RUNTIME_MISSING, REPLACE_GAME, RELEASE_UNAVAILABLE, DEV_INTEROP,
        GAME_NOT_ADAPTED, UPDATE_GAME
    }

    static final class Failure {
        final boolean launch;
        final String message;

        Failure(boolean launch, String message) {
            this.launch = launch;
            this.message = message;
        }
    }

    static final class AppUpdate {
        final String version;
        final String url;

        AppUpdate(String version, String url) {
            this.version = version;
            this.url = url;
        }
    }

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

    static final class Facts {
        final LaunchGate.Decision decision;
        final String gameVersion;
        final String adaptedVersion;
        final boolean fromCache;
        final boolean lastInstallFailed;
        final boolean runtimeMissing;
        final AppUpdate appUpdate;
        final boolean playInstalled;
        final boolean playSource;

        Facts(LaunchGate.Decision decision, String gameVersion, String adaptedVersion, boolean fromCache,
                boolean lastInstallFailed, boolean runtimeMissing, AppUpdate appUpdate,
                boolean playInstalled, boolean playSource) {
            this.decision = decision;
            this.gameVersion = gameVersion;
            this.adaptedVersion = adaptedVersion;
            this.fromCache = fromCache;
            this.lastInstallFailed = lastInstallFailed;
            this.runtimeMissing = runtimeMissing;
            this.appUpdate = appUpdate;
            this.playInstalled = playInstalled;
            this.playSource = playSource;
        }
    }

    final Status status;
    final Action primary;
    final Action source;
    final List<Note> notes;
    final AppUpdate appUpdate;
    final Failure failure;
    final Progress progress;
    final String gameVersion;
    final String adaptedVersion;
    final boolean busy;

    private LauncherScreen(Status status, Action primary, Action source, List<Note> notes, AppUpdate appUpdate,
            Failure failure, Progress progress, String gameVersion, String adaptedVersion, boolean busy) {
        this.status = status;
        this.primary = primary;
        this.source = source;
        this.notes = Collections.unmodifiableList(notes);
        this.appUpdate = appUpdate;
        this.failure = failure;
        this.progress = progress;
        this.gameVersion = gameVersion;
        this.adaptedVersion = adaptedVersion;
        this.busy = busy;
    }

    static LauncherScreen checking(Facts last) {
        return new LauncherScreen(Status.CHECKING, null, last == null ? null : sourceRow(last), List.of(), last == null ? null : last.appUpdate, null,
                null, last == null ? null : last.gameVersion, last == null ? null : last.adaptedVersion, true);
    }

    static LauncherScreen checkFailed(String message, boolean officialInstalled) {
        return new LauncherScreen(null, Action.RETRY, officialInstalled ? Action.OPEN_OFFICIAL : null,
                List.of(), null, new Failure(false, message), null, null, null, false);
    }

    static LauncherScreen launching(Facts facts, Progress progress) {
        return new LauncherScreen(progress == null ? Status.PREPARING : Status.DOWNLOADING, null, sourceRow(facts), List.of(),
                facts.appUpdate, null, progress, facts.gameVersion, facts.adaptedVersion, true);
    }

    static LauncherScreen launchFailed(Facts facts, String message) {
        LauncherScreen ready = ready(facts);
        return new LauncherScreen(ready.status, Action.RETRY, ready.source, ready.notes, ready.appUpdate,
                new Failure(true, message), null, ready.gameVersion, ready.adaptedVersion, false);
    }

    static LauncherScreen ready(Facts facts) {
        LaunchGate.Decision decision = facts.decision;
        List<Note> notes = new ArrayList<>();
        Status status;
        Action primary = null;
        AppUpdate update = facts.appUpdate;

        if (decision.bppLaunch()) {
            status = Status.READY;
            if (decision.developmentInterop) notes.add(Note.DEV_INTEROP);
            primary = play(facts, notes);
        } else {
            switch (decision.tier) {
                case NOT_INSTALLED:
                    status = Status.NOT_INSTALLED;
                    primary = facts.playInstalled ? Action.INSTALL_FROM_PLAY : Action.DOWNLOAD_FROM_PUBLISHER;
                    break;
                case ANDROID_RELEASE_APK:
                    status = Status.REPLACE;
                    notes.add(Note.REPLACE_GAME);
                    primary = facts.playInstalled ? Action.INSTALL_FROM_PLAY : Action.DOWNLOAD_FROM_PUBLISHER;
                    break;
                case OFFLINE_NO_CACHE:
                    status = Status.OFFLINE;
                    notes.add(Note.RELEASE_UNAVAILABLE);
                    primary = Action.RETRY;
                    break;
                case OLDER:
                    status = Status.GAME_UPDATE;
                    notes.add(Note.UPDATE_GAME);
                    primary = facts.playInstalled && facts.playSource
                            ? Action.UPDATE_FROM_PLAY : Action.DOWNLOAD_FROM_PUBLISHER;
                    break;
                case NEWER_WAITING:
                    status = Status.WAITING;
                    notes.add(Note.GAME_NOT_ADAPTED);
                    break;
                case NORMAL:
                default:
                    throw new IllegalStateException(decision + " always allows a BPP launch");
            }
        }
        if (facts.fromCache) notes.add(Note.FROM_CACHE);
        return new LauncherScreen(status, primary, sourceRow(facts), notes, update, null, null, facts.gameVersion,
                facts.adaptedVersion, false);
    }

    /** A different runtime digest matters because the Updater rejects Mods for another runtime (ADR 0019). */
    static AppUpdate appUpdate(String ownDigest, long ownVersionCode, String releaseDigest,
            long releaseVersionCode, String releaseVersion, String url) {
        if (ownDigest == null || releaseVersionCode <= ownVersionCode || ownDigest.equals(releaseDigest)) return null;
        return new AppUpdate(releaseVersion, url);
    }

    static Action sourceRow(Facts facts) {
        if (facts.decision.nativeLaunch()) return Action.OPEN_OFFICIAL;
        return facts.playInstalled ? Action.DOWNLOAD_FROM_PUBLISHER : null;
    }

    private static Action play(Facts facts, List<Note> notes) {
        if (facts.lastInstallFailed && !facts.decision.developmentInterop) notes.add(Note.LAST_INSTALL_FAILED);
        if (facts.runtimeMissing) {
            notes.add(Note.RUNTIME_MISSING);
            return null;
        }
        return Action.PLAY;
    }
}
