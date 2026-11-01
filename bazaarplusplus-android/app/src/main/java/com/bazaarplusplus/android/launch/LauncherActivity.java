package com.bazaarplusplus.android.launch;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.view.View;
import android.widget.Button;
import android.widget.TextView;

import com.bazaarplusplus.android.BuildConfigFacts;
import com.bazaarplusplus.android.R;
import com.bazaarplusplus.android.diag.AppLog;
import com.bazaarplusplus.android.diag.Diagnostics;
import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.game.InstalledGame;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * The App's start screen. It checks the installed game against the release ({@link LaunchCheck},
 * {@link LaunchGate}) and offers what the tier allows: BPP launch, the Compatibility Mode opt-in,
 * native launch (the official App, untouched), the official game's install or update links, the
 * new App, and the diagnostics export.
 */
public final class LauncherActivity extends Activity {
    static final String TAG = "BppLauncher";

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private TextView status;
    private TextView detail;
    private Button primary;
    private Button secondary;
    private Button nativeLaunch;
    private Button diagnostics;
    private LaunchCheck check;
    private boolean busy;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.launcher);
        status = findViewById(R.id.status);
        detail = findViewById(R.id.detail);
        primary = findViewById(R.id.primary);
        secondary = findViewById(R.id.secondary);
        nativeLaunch = findViewById(R.id.native_launch);
        diagnostics = findViewById(R.id.diagnostics);
        ((TextView) findViewById(R.id.version)).setText(getString(R.string.app_version,
                BuildConfigFacts.versionName(this)));
        nativeLaunch.setOnClickListener(v -> launchNative());
        diagnostics.setOnClickListener(v -> exportDiagnostics());
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (!busy) recheck();
    }

    @Override
    protected void onDestroy() {
        worker.shutdown();
        super.onDestroy();
    }

    private void recheck() {
        busy = true;
        show(getString(R.string.checking), List.of());
        hide(primary, secondary, nativeLaunch);
        worker.execute(() -> {
            try {
                LaunchCheck result = LaunchCheck.run(this);
                ui(() -> {
                    busy = false;
                    check = result;
                    render(result);
                });
            } catch (Throwable t) {
                AppLog.e(TAG, "launch check failed", t);
                ui(() -> {
                    busy = false;
                    show(getString(R.string.check_failed, String.valueOf(t.getMessage())), List.of());
                    button(secondary, R.string.retry, v -> recheck());
                    nativeLaunch.setVisibility(View.VISIBLE);
                });
            }
        });
    }

    private void render(LaunchCheck check) {
        hide(primary, secondary, nativeLaunch);
        List<String> notes = new ArrayList<>();
        LaunchGate.Decision decision = check.decision;
        if (decision.nativeLaunch()) nativeLaunch.setVisibility(View.VISIBLE);
        String game = check.game == null ? null : check.game.versionName;

        if (check.devInterop != null && check.game != null) {
            bppLaunch(check, getString(R.string.dev_interop, game), notes);
            return;
        }
        switch (decision.tier) {
            case NOT_INSTALLED:
                show(getString(R.string.game_missing), notes);
                button(primary, R.string.install_from_play, v -> open(getString(R.string.url_play)));
                button(secondary, R.string.download_from_publisher, v -> open(getString(R.string.url_publisher)));
                return;
            case OFFLINE_NO_CACHE:
                show(getString(R.string.release_unavailable), notes);
                button(secondary, R.string.retry, v -> recheck());
                return;
            case NO_RELEASE:
                show(getString(R.string.no_release), notes);
                return;
            case APP_UPDATE_REQUIRED:
                show(getString(R.string.app_update_required), notes);
                button(primary, R.string.install_app, v -> open(check.manifest.manifest.app.file.url));
                return;
            case OLDER:
                show(getString(R.string.game_older, game, check.manifest.manifest.game.gameVersion), notes);
                button(primary, R.string.update_from_play, v -> open(getString(R.string.url_play)));
                button(secondary, R.string.download_from_publisher, v -> open(getString(R.string.url_publisher)));
                return;
            case NEWER_WAITING:
                show(getString(R.string.game_waiting, game), List.of(getString(R.string.game_waiting_detail)));
                return;
            case NEWER_COMPAT_AVAILABLE:
                notes.add(getString(R.string.compat_explain));
                if (!decision.compatibilityChosen) {
                    show(getString(R.string.game_not_adapted, game), notes);
                    button(primary, R.string.compat_opt_in, v -> chooseCompatibility());
                    return;
                }
                bppLaunch(check, getString(R.string.compat_on, game), notes);
                return;
            case NORMAL:
            default:
                bppLaunch(check, getString(R.string.game_found, game), notes);
        }
    }

    private void bppLaunch(LaunchCheck check, String state, List<String> notes) {
        if (check.manifest != null && check.manifest.fromCache) notes.add(getString(R.string.release_from_cache));
        // A Development interop stages nothing, so the staging note does not apply to it.
        if (check.lastModsInstallFailed && check.devInterop == null) notes.add(getString(R.string.last_install_failed));
        if (check.facts == null) notes.add(getString(R.string.runtime_missing));
        show(state, notes);
        button(primary, R.string.bpp_launch, v -> launchBpp());
        primary.setEnabled(check.facts != null);
    }

    private void chooseCompatibility() {
        LaunchCheck current = check;
        worker.execute(() -> {
            try {
                new CompatibilityChoices(AppFiles.compatibilityChoices(this)).choose(current.hash);
                AppLog.i(TAG, "Compatibility Mode chosen for " + current.hash);
            } catch (Throwable t) {
                AppLog.e(TAG, "cannot keep the Compatibility Mode choice", t);
            }
            ui(this::recheck);
        });
    }

    private void launchBpp() {
        LaunchCheck current = check;
        busy = true;
        primary.setEnabled(false);
        secondary.setEnabled(false);
        nativeLaunch.setEnabled(false);
        show(getString(R.string.preparing), List.of());
        worker.execute(() -> {
            try {
                BppLaunch.prepare(this, current, new BppLaunch.Progress() {
                    @Override
                    public void preparing() {
                        ui(() -> show(getString(R.string.preparing), List.of()));
                    }

                    @Override
                    public void downloading(boolean interop, long received, long total) {
                        ui(() -> show(getString(interop ? R.string.downloading_interop : R.string.downloading_plugin,
                                getString(R.string.size_mb, received / 1e6), getString(R.string.size_mb, total / 1e6)),
                                List.of()));
                    }
                });
                ui(() -> {
                    Intent intent = new Intent();
                    intent.setClassName(this, GameSession.GAME_ACTIVITY);
                    startActivity(intent);
                    finish();
                });
            } catch (Throwable t) {
                AppLog.e(TAG, "BPP launch failed", t);
                ui(() -> {
                    busy = false;
                    show(getString(R.string.launch_failed, String.valueOf(t.getMessage())), List.of());
                    primary.setEnabled(true);
                    secondary.setEnabled(true);
                    nativeLaunch.setEnabled(true);
                    button(primary, R.string.retry, v -> recheck());
                });
            }
        });
    }

    private void launchNative() {
        Intent intent = getPackageManager().getLaunchIntentForPackage(InstalledGame.PACKAGE);
        if (intent == null) {
            open(getString(R.string.url_play));
            return;
        }
        startActivity(intent);
    }

    private void exportDiagnostics() {
        LaunchCheck current = check;
        diagnostics.setEnabled(false);
        CharSequence before = status.getText();
        status.setText(R.string.exporting_diagnostics);
        worker.execute(() -> {
            String message;
            try {
                String state = "app: " + getPackageName() + " " + BuildConfigFacts.versionName(this) + " ("
                        + BuildConfigFacts.versionCode(this) + ")\n"
                        + (current == null ? "tier: not checked\n" : current.describe());
                String name = Diagnostics.export(this, Diagnostics.collect(AppFiles.external(this), AppLog.file(),
                        LaunchRecord.PATH, state));
                AppLog.i(TAG, "exported diagnostics " + name);
                message = getString(R.string.diagnostics_exported, name);
            } catch (Throwable t) {
                AppLog.e(TAG, "diagnostics export failed", t);
                message = getString(R.string.diagnostics_failed, String.valueOf(t.getMessage()));
            }
            String shown = message;
            ui(() -> {
                diagnostics.setEnabled(true);
                status.setText(before);
                detail.setText(shown);
                detail.setVisibility(View.VISIBLE);
            });
        });
    }

    private void open(String url) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        } catch (ActivityNotFoundException e) {
            AppLog.e(TAG, "nothing opens " + url, e);
        }
    }

    private void show(String state, List<String> notes) {
        status.setText(state);
        detail.setText(String.join("\n", notes));
        detail.setVisibility(notes.isEmpty() ? View.GONE : View.VISIBLE);
    }

    private static void button(Button button, int text, View.OnClickListener listener) {
        button.setText(text);
        button.setOnClickListener(listener);
        button.setEnabled(true);
        button.setVisibility(View.VISIBLE);
    }

    private static void hide(View... views) {
        for (View view : views) {
            view.setVisibility(View.GONE);
            view.setEnabled(true);
        }
    }

    private void ui(Runnable action) {
        runOnUiThread(() -> {
            if (!isDestroyed()) action.run();
        });
    }
}
