package com.bazaarplusplus.android.launch;

import android.content.Context;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;

import com.bazaarplusplus.android.BuildConfigFacts;
import com.bazaarplusplus.android.diag.AppLog;
import com.bazaarplusplus.android.diag.Diagnostics;
import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.game.InstalledGame;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * The launcher's work and state for the whole process: the launch check, a BPP launch, the
 * diagnostics export and the {@link LauncherScreen} they produce. It outlives any one
 * {@link LauncherActivity}, so a fold, a text-size change or a recreated Activity
 * re-renders the same screen and never starts a second check or launch while one runs
 * ({@code GameSession.prepare} runs once per process). State is read and written on the main
 * thread only; the work runs on one worker thread.
 */
final class LauncherSession {
    /** The attached Activity. */
    interface Listener {
        void changed();

        /** The game is prepared: start the bridge Activity and leave the launcher. */
        void startGame();
    }

    private static LauncherSession instance;

    static LauncherSession get() {
        if (instance == null) instance = new LauncherSession();
        return instance;
    }

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler main = new Handler(Looper.getMainLooper());
    private Listener listener;

    /** Null until the first check starts. */
    private LauncherScreen screen;
    private LaunchCheck check;
    private LauncherScreen.Facts facts;
    /** The last diagnostics export's result line, or null. */
    private String diagnostics;
    private boolean exporting;

    private LauncherSession() {}

    LauncherScreen screen() {
        return screen;
    }

    LaunchCheck check() {
        return check;
    }

    String diagnostics() {
        return diagnostics;
    }

    boolean exporting() {
        return exporting;
    }

    boolean busy() {
        return screen != null && screen.busy;
    }

    void attach(Listener listener) {
        this.listener = listener;
    }

    void detach(Listener listener) {
        if (this.listener == listener) this.listener = null;
    }

    /** Runs the launch check unless a check or a launch is already running. */
    void recheck(Context context) {
        if (busy()) return;
        Context app = context.getApplicationContext();
        show(LauncherScreen.checking(facts));
        worker.execute(() -> {
            try {
                LaunchCheck result = LaunchCheck.run(app);
                LauncherScreen.Facts next = facts(app, result);
                post(() -> {
                    check = result;
                    facts = next;
                    show(LauncherScreen.ready(next));
                });
            } catch (Throwable t) {
                AppLog.e(LauncherActivity.TAG, "launch check failed", t);
                boolean installed = app.getPackageManager().getLaunchIntentForPackage(InstalledGame.PACKAGE) != null;
                post(() -> show(LauncherScreen.checkFailed(String.valueOf(t.getMessage()), installed)));
            }
        });
    }

    void chooseCompatibility(Context context) {
        LaunchCheck current = check;
        if (current == null || busy()) return;
        Context app = context.getApplicationContext();
        worker.execute(() -> {
            try {
                new CompatibilityChoices(AppFiles.compatibilityChoices(app)).choose(current.hash);
                AppLog.i(LauncherActivity.TAG, "Compatibility Mode chosen for " + current.hash);
            } catch (Throwable t) {
                AppLog.e(LauncherActivity.TAG, "cannot keep the Compatibility Mode choice", t);
            }
            post(() -> recheck(app));
        });
    }

    void launch(Context context) {
        LaunchCheck current = check;
        LauncherScreen.Facts currentFacts = facts;
        if (current == null || currentFacts == null || busy()) return;
        Context app = context.getApplicationContext();
        show(LauncherScreen.launching(currentFacts, null));
        worker.execute(() -> {
            try {
                BppLaunch.prepare(app, current, new BppLaunch.Progress() {
                    @Override
                    public void preparing() {
                        post(() -> show(LauncherScreen.launching(currentFacts, null)));
                    }

                    @Override
                    public void downloading(boolean interop, long received, long total) {
                        post(() -> show(LauncherScreen.launching(currentFacts,
                                new LauncherScreen.Progress(interop, received, total))));
                    }
                });
                post(() -> {
                    // Not busy any more: a launcher reopened in this process checks again (and a
                    // second launch then says the game already ran, as GameSession requires).
                    screen = LauncherScreen.ready(currentFacts);
                    if (listener != null) {
                        listener.startGame();
                    } else {
                        app.startActivity(new Intent().setClassName(app, GameSession.GAME_ACTIVITY)
                                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                    }
                });
            } catch (Throwable t) {
                AppLog.e(LauncherActivity.TAG, "BPP launch failed", t);
                post(() -> show(LauncherScreen.launchFailed(currentFacts, String.valueOf(t.getMessage()))));
            }
        });
    }

    void exportDiagnostics(Context context, String exportedFormat, String failedFormat) {
        if (exporting) return;
        LaunchCheck current = check;
        Context app = context.getApplicationContext();
        exporting = true;
        notifyChanged();
        worker.execute(() -> {
            String message;
            try {
                String state = "app: " + app.getPackageName() + " " + BuildConfigFacts.versionName(app) + " ("
                        + BuildConfigFacts.versionCode(app) + ")\n"
                        + (current == null ? "tier: not checked\n" : current.describe());
                String name = Diagnostics.export(app, Diagnostics.collect(AppFiles.external(app), AppLog.file(),
                        LaunchRecord.PATH, state));
                AppLog.i(LauncherActivity.TAG, "exported diagnostics " + name);
                message = String.format(Locale.ROOT, exportedFormat, name);
            } catch (Throwable t) {
                AppLog.e(LauncherActivity.TAG, "diagnostics export failed", t);
                message = String.format(Locale.ROOT, failedFormat, String.valueOf(t.getMessage()));
            }
            String shown = message;
            post(() -> {
                exporting = false;
                diagnostics = shown;
                notifyChanged();
            });
        });
    }

    /** The facts of a check that the screen shows, worked out on the worker thread. */
    private static LauncherScreen.Facts facts(Context app, LaunchCheck check) {
        PlatformManifest manifest = check.manifest == null ? null : check.manifest.manifest;
        LauncherScreen.AppUpdate update = null;
        if (manifest != null && manifest.app != null) {
            // Only a release build carries a runtime digest; a development build is never reminded.
            boolean releaseBuild = check.facts != null && check.facts.nonManagedPayloadDigest != null;
            update = LauncherScreen.appUpdate(releaseBuild, BuildConfigFacts.versionCode(app), manifest.app.versionCode,
                    manifest.version, manifest.app.file.url);
        }
        return new LauncherScreen.Facts(check.decision, check.game == null ? null : check.game.versionName,
                manifest == null ? null : manifest.game.gameVersion, check.manifest != null && check.manifest.fromCache,
                check.lastModsInstallFailed, check.facts == null, check.devInterop != null, update);
    }

    private void show(LauncherScreen next) {
        screen = next;
        notifyChanged();
    }

    private void notifyChanged() {
        if (listener != null) listener.changed();
    }

    private void post(Runnable action) {
        main.post(action);
    }
}
