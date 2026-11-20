package com.bazaarplusplus.android.launch;

import android.content.Context;
import android.content.Intent;
import android.content.res.Resources;
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

// The session survives Activity recreation so a configuration change cannot start a second launch.
// The main thread owns the state. The worker does the blocking work.
final class LauncherSession {
    interface Listener {
        void changed();

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

    private LauncherScreen screen;
    private LaunchCheck check;
    private LauncherScreen.Facts facts;
    private Export export;
    private boolean exporting;
    private CoverArt.Images cover;
    private boolean coverRequested;

    static final class Export {
        final String message;
        final Diagnostics.Exported saved;

        Export(String message, Diagnostics.Exported saved) {
            this.message = message;
            this.saved = saved;
        }
    }

    private LauncherSession() {}

    LauncherScreen screen() {
        return screen;
    }

    LaunchCheck check() {
        return check;
    }

    Export takeExport() {
        Export taken = export;
        export = null;
        return taken;
    }

    CoverArt.Images cover() {
        return cover;
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

    /** Decode on a separate thread so cover art never delays a launch check. */
    void loadCover(Context context, int minHeight) {
        if (coverRequested) return;
        coverRequested = true;
        Resources resources = context.getApplicationContext().getResources();
        new Thread(() -> {
            CoverArt.Images art = CoverArt.load(resources, minHeight);
            post(() -> {
                cover = art;
                notifyChanged();
            });
        }, "bpp-cover").start();
    }

    void launch(Context context) {
        LaunchCheck current = check;
        LauncherScreen.Facts currentFacts = facts;
        if (current == null || currentFacts == null || busy()) return;
        Context app = context.getApplicationContext();
        show(LauncherScreen.launching(currentFacts, null));
        worker.execute(() -> {
            try {
                BppLaunch launch = new BppLaunch(new AndroidLaunchHost(app, current.game, current.facts),
                        AppFiles.launchDirs(app));
                launch.prepare(current.inputs(), new BppLaunch.Progress() {
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
                    screen = LauncherScreen.ready(currentFacts);
                    // Release the bitmaps because the game shares the launcher's process.
                    cover = null;
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
            Diagnostics.Exported saved = null;
            try {
                String state = "app: " + app.getPackageName() + " " + BuildConfigFacts.versionName(app) + " ("
                        + BuildConfigFacts.versionCode(app) + ")\n"
                        + (current == null ? "tier: not checked\n" : current.describe());
                saved = Diagnostics.export(app, Diagnostics.collect(AppFiles.external(app), AppLog.file(),
                        LaunchRecord.PATH, state));
                AppLog.i(LauncherActivity.TAG, "exported diagnostics " + saved.name);
                message = String.format(Locale.ROOT, exportedFormat, saved.name);
            } catch (Throwable t) {
                AppLog.e(LauncherActivity.TAG, "diagnostics export failed", t);
                message = String.format(Locale.ROOT, failedFormat, String.valueOf(t.getMessage()));
            }
            Export done = new Export(message, saved);
            post(() -> {
                exporting = false;
                export = done;
                notifyChanged();
            });
        });
    }

    private static LauncherScreen.Facts facts(Context app, LaunchCheck check) {
        PlatformManifest manifest = check.manifest == null ? null : check.manifest.manifest;
        LauncherScreen.AppUpdate update = null;
        if (manifest != null && manifest.app != null) {
            String ownDigest = check.facts == null ? null : check.facts.nonManagedPayloadDigest;
            update = LauncherScreen.appUpdate(ownDigest, BuildConfigFacts.versionCode(app),
                    manifest.nonManagedPayloadDigest, manifest.app.versionCode, manifest.version, manifest.app.file.url);
        }
        PlatformManifest.Adaptation adaptation = check.adaptation();
        return new LauncherScreen.Facts(check.decision, check.game == null ? null : check.game.versionName,
                adaptation == null ? null : adaptation.game.gameVersion, check.manifest != null && check.manifest.fromCache,
                check.lastModsInstallFailed, check.facts == null, update,
                InstalledGame.playStoreInstalled(app.getPackageManager()),
                adaptation != null && PlatformManifest.PLAY.equals(adaptation.source));
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
