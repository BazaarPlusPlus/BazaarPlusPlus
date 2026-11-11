package com.bazaarplusplus.android.launch;

import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.app.Activity;
import android.app.Dialog;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.Insets;
import android.net.Uri;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.PopupMenu;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import com.bazaarplusplus.android.BuildConfigFacts;
import com.bazaarplusplus.android.DevOverrides;
import com.bazaarplusplus.android.R;
import com.bazaarplusplus.android.diag.AppLog;
import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.game.InstalledGame;

import java.util.ArrayList;
import java.util.List;

/**
 * The App's start screen (design/components/Launcher). It renders {@link LauncherSession}'s
 * {@link LauncherScreen} into the layout the window width picks (one column; the cover over two
 * panes from {@code pane-breakpoint}; cover and content side by side in a window wider than
 * tall from {@code cover-pane-breakpoint}) and hands taps back to the session. The Activity handles size
 * and text-scale changes itself (see the manifest), so a fold re-renders the screen without
 * recreating it; the work and its state live in the session either way.
 */
public final class LauncherActivity extends Activity implements LauncherSession.Listener {
    static final String TAG = "BppLauncher";
    /** Android font scale from which the panes would be too narrow for their text. */
    private static final float PANES_MAX_FONT_SCALE = 1.3f;
    private static final String PREFERENCES = "launcher";
    /** The App version whose update reminder was shown; each version reminds once. */
    private static final String REMINDED_VERSION = "reminded_version";

    private final LauncherSession session = LauncherSession.get();
    private LauncherLayout layout;
    private boolean largeText;
    private Insets insets = Insets.NONE;
    private ObjectAnimator pulse;
    private PopupMenu menu;
    private Dialog dialog;
    private boolean devLoginStarted;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        // Android 15 draws apps edge to edge; do the same on older versions and pad by the insets.
        getWindow().setDecorFitsSystemWindows(false);
        inflate(0);
        session.attach(this);
        session.loadCover(this, getResources().getDisplayMetrics().heightPixels * 3 / 5);
    }

    @Override
    protected void onResume() {
        super.onResume();
        // Coming back from Google Play or the official game: the installed game may have changed.
        session.recheck(this);
    }

    @Override
    protected void onDestroy() {
        session.detach(this);
        stopPulse();
        if (menu != null) menu.dismiss();
        if (dialog != null) dialog.dismiss();
        super.onDestroy();
    }

    @Override
    public void onConfigurationChanged(Configuration config) {
        super.onConfigurationChanged(config);
        if (menu != null) menu.dismiss();
        ScrollView scroll = findViewById(R.id.scroll);
        inflate(scroll == null ? 0 : scroll.getScrollY());
    }

    @Override
    public void changed() {
        render();
        LauncherScreen screen = session.screen();
        if (devLoginStarted && screen != null && screen.failure != null && DevOverrides.loginRequested(this)) {
            DevOverrides.rejectLogin(this, "launch_failed");
        }
        if (!devLoginStarted && screen != null && !screen.busy && DevOverrides.loginRequested(this)) {
            devLoginStarted = true;
            if (screen.primary == LauncherScreen.Action.PLAY) session.launch(this);
            else DevOverrides.rejectLogin(this, screen.status == null ? "check_failed" : screen.status.name());
        }
    }

    @Override
    public void startGame() {
        startActivity(new Intent().setClassName(this, GameSession.GAME_ACTIVITY));
        finish();
    }

    /** Picks the layout for the current window and text size, then renders the session into it. */
    private void inflate(int scrollY) {
        Configuration config = getResources().getConfiguration();
        largeText = config.fontScale >= PANES_MAX_FONT_SCALE;
        layout = LauncherLayout.of(config.screenWidthDp, config.screenHeightDp, largeText,
                dp(R.dimen.bpp_pane_breakpoint), dp(R.dimen.bpp_cover_pane_breakpoint));
        stopPulse();
        setContentView(layout == LauncherLayout.COLUMN ? R.layout.launcher
                : layout == LauncherLayout.PANES ? R.layout.launcher_panes : R.layout.launcher_wide);
        View root = findViewById(R.id.root);
        root.setOnApplyWindowInsetsListener((view, windowInsets) -> {
            insets = windowInsets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            pad();
            return WindowInsets.CONSUMED;
        });
        root.requestApplyInsets();

        if (layout == LauncherLayout.COLUMN && largeText) {
            // Accessibility text sizes: the stats as rows, so long values never squeeze.
            View stats = findViewById(R.id.stats);
            ViewGroup parent = (ViewGroup) stats.getParent();
            int index = parent.indexOfChild(stats);
            parent.removeViewAt(index);
            View rows = getLayoutInflater().inflate(R.layout.launcher_info, parent, false);
            rows.setId(R.id.rows);
            parent.addView(rows, index);
        } else if (layout == LauncherLayout.PANES) {
            // Rows sit beside the end pane's buttons without dividers; the wide content pane keeps them.
            ((LinearLayout) findViewById(R.id.rows)).setShowDividers(LinearLayout.SHOW_DIVIDER_NONE);
            // The end pane is at least as tall as the start pane, so its primary lines up with the
            // last row (a match_parent pane would be cut to the start pane's height instead).
            View end = findViewById(R.id.end_pane);
            findViewById(R.id.start_pane).addOnLayoutChangeListener((view, left, top, right, bottom, oldLeft, oldTop,
                    oldRight, oldBottom) -> {
                if (end.getMinimumHeight() != bottom - top) end.setMinimumHeight(bottom - top);
            });
        }
        String version = getString(R.string.version_value, BuildConfigFacts.versionName(this));
        for (TextView app : views(R.id.info_app)) app.setText(version);
        findViewById(R.id.source_icon).setClipToOutline(true);

        View more = findViewById(R.id.more);
        if (layout == LauncherLayout.WIDE) more.setBackgroundResource(R.drawable.bpp_more_bare);
        more.setOnClickListener(this::showMenu);
        render();
        View scroll = findViewById(R.id.scroll);
        scroll.post(() -> scroll.scrollTo(0, scrollY));
    }

    /** Keeps the panel's content and More clear of the system bars and the cutout; the cover runs under them. */
    private void pad() {
        View panel = findViewById(R.id.panel);
        int gutter = getResources().getDimensionPixelSize(layout == LauncherLayout.COLUMN ? R.dimen.bpp_space_16 : R.dimen.bpp_space_24);
        int top = getResources().getDimensionPixelSize(R.dimen.bpp_space_24);
        int bottom = getResources().getDimensionPixelSize(layout == LauncherLayout.WIDE ? R.dimen.bpp_space_24 : R.dimen.bpp_space_16);
        if (layout == LauncherLayout.WIDE) {
            panel.setPadding(gutter, insets.top + top, insets.right + gutter, insets.bottom + bottom);
        } else {
            panel.setPadding(insets.left + gutter, top, insets.right + gutter, insets.bottom + bottom);
        }
        ViewGroup.MarginLayoutParams more = (ViewGroup.MarginLayoutParams) findViewById(R.id.more).getLayoutParams();
        int edge = getResources().getDimensionPixelSize(R.dimen.bpp_space_8);
        more.topMargin = insets.top + edge;
        more.setMarginEnd(insets.right + edge);
        findViewById(R.id.more).setLayoutParams(more);
    }

    private void render() {
        LauncherScreen screen = session.screen();
        if (screen == null) return;
        LaunchCheck check = session.check();

        renderCover();
        renderStatus(screen);
        renderFailure(screen);
        renderSource(screen.source, check);
        renderProgress(screen.progress);
        bind(findViewById(R.id.primary), screen.primary);
        // An empty action area would add its 16 gap under the row above it.
        findViewById(R.id.action).setVisibility(screen.progress == null && screen.primary == null ? View.GONE : View.VISIBLE);

        List<String> notes = new ArrayList<>();
        for (LauncherScreen.Note note : screen.notes) notes.add(noteLine(note, screen));
        TextView notesView = findViewById(R.id.notes);
        notesView.setText(String.join("\n", notes));
        notesView.setVisibility(notes.isEmpty() ? View.GONE : View.VISIBLE);

        for (TextView game : views(R.id.info_game)) game.setText(valueOrEmpty(screen.gameVersion));
        for (TextView adapted : views(R.id.info_adapted)) adapted.setText(valueOrEmpty(screen.adaptedVersion));

        remind(screen.appUpdate, screen.adaptedVersion);
        LauncherSession.Export export = session.takeExport();
        if (export != null) shareDiagnostics(export);
    }

    /** The stats as columns and as rows: the one-column layout carries both and shows one. */
    private List<TextView> views(int id) {
        List<TextView> found = new ArrayList<>();
        for (int container : new int[] {R.id.stats, R.id.rows}) {
            View parent = findViewById(container);
            if (parent != null) found.add(parent.findViewById(id));
        }
        return found;
    }

    private void renderCover() {
        ((CoverView) findViewById(R.id.cover)).setArt(session.cover());
    }

    private void renderFailure(LauncherScreen screen) {
        View error = findViewById(R.id.error_banner);
        if (screen.failure == null) {
            error.setVisibility(View.GONE);
            return;
        }
        error.setVisibility(View.VISIBLE);
        error.setBackgroundResource(R.drawable.bpp_banner_error);
        ((ImageView) error.findViewById(R.id.banner_icon)).setImageResource(R.drawable.bpp_icon_error);
        ((TextView) error.findViewById(R.id.banner_message)).setText(getString(
                screen.failure.launch ? R.string.launch_failed : R.string.check_failed, screen.failure.message));
        error.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_ASSERTIVE);
    }

    /**
     * The source row: the official game with its own icon, or the publisher's APK beside a Google
     * Play primary, shown with the promotional art.
     */
    private void renderSource(LauncherScreen.Action source, LaunchCheck check) {
        View row = findViewById(R.id.source);
        if (source == null) {
            row.setVisibility(View.GONE);
            return;
        }
        row.setVisibility(View.VISIBLE);
        ImageView icon = findViewById(R.id.source_icon);
        if (source == LauncherScreen.Action.OPEN_OFFICIAL && check != null && check.game != null) {
            icon.setImageDrawable(check.game.info.loadIcon(getPackageManager()));
        } else {
            CoverArt.Images art = session.cover();
            icon.setImageBitmap(art == null ? null : art.promo);
        }
        ((TextView) findViewById(R.id.source_label)).setText(actionText(source));
        row.setOnClickListener(v -> perform(source));
    }

    private void renderStatus(LauncherScreen screen) {
        View badge = findViewById(R.id.status_badge);
        if (screen.status == null) {
            badge.setVisibility(View.GONE);
            stopPulse();
            return;
        }
        badge.setVisibility(View.VISIBLE);
        int[] colors = toneColors(screen.status.tone);
        badge.setBackgroundTintList(ColorStateList.valueOf(getColor(colors[1])));
        View dot = findViewById(R.id.status_dot);
        dot.setBackgroundTintList(ColorStateList.valueOf(getColor(colors[0])));
        TextView text = findViewById(R.id.status_text);
        text.setTextColor(getColor(colors[0]));
        String word = getString(statusText(screen.status));
        text.setText(screen.progress == null ? word
                : getString(R.string.status_with_percent, word, screen.progress.percent()));
        if (screen.status.tone == LauncherScreen.Tone.BUSY) startPulse(dot);
        else stopPulse();
    }

    private void renderProgress(LauncherScreen.Progress progress) {
        ProgressBar bar = findViewById(R.id.progress);
        TextView meta = findViewById(R.id.progress_meta);
        if (progress == null) {
            bar.setVisibility(View.GONE);
            meta.setVisibility(View.GONE);
            return;
        }
        bar.setVisibility(View.VISIBLE);
        bar.setProgress(progress.percent());
        meta.setVisibility(View.VISIBLE);
        meta.setText(getString(progress.interop ? R.string.progress_interop : R.string.progress_plugin,
                getString(R.string.size_mb, progress.received / 1e6), getString(R.string.size_mb, progress.total / 1e6)));
    }

    /** A newer App: a dialog once per version, never a gate. */
    private void remind(LauncherScreen.AppUpdate update, String adaptedVersion) {
        if (update == null || (dialog != null && dialog.isShowing())) return;
        SharedPreferences preferences = getSharedPreferences(PREFERENCES, MODE_PRIVATE);
        if (update.version.equals(preferences.getString(REMINDED_VERSION, null))) return;
        preferences.edit().putString(REMINDED_VERSION, update.version).apply();
        showDialog(getString(R.string.app_update_title, update.version),
                adaptedVersion == null ? null : getString(R.string.app_update_message, adaptedVersion),
                R.string.later, update.url == null ? 0 : R.string.download, () -> open(update.url));
    }

    private void showMenu(View anchor) {
        menu = new PopupMenu(this, anchor);
        menu.inflate(R.menu.launcher_more);
        menu.setOnMenuItemClickListener(item -> {
            if (item.getItemId() == R.id.menu_diagnostics) {
                session.exportDiagnostics(this, getString(R.string.diagnostics_exported),
                        getString(R.string.diagnostics_failed));
            } else {
                showDialog(getString(R.string.app_name),
                        getString(R.string.about_message, BuildConfigFacts.versionName(this)),
                        R.string.close, R.string.open_website, () -> open(getString(R.string.url_site)));
            }
            return true;
        });
        menu.setOnDismissListener(dismissed -> menu = null);
        menu.show();
    }

    /** The launcher's dialog: a title, an optional sentence, a text button and an optional primary. */
    private void showDialog(String title, String message, int dismiss, int confirm, Runnable action) {
        if (dialog != null) dialog.dismiss();
        dialog = new Dialog(this, R.style.Theme_Bpp_Dialog);
        dialog.setContentView(R.layout.launcher_dialog);
        ((TextView) dialog.findViewById(R.id.dialog_title)).setText(title);
        TextView body = dialog.findViewById(R.id.dialog_message);
        body.setText(message);
        body.setVisibility(message == null ? View.GONE : View.VISIBLE);
        Button no = dialog.findViewById(R.id.dialog_dismiss);
        no.setText(dismiss);
        no.setOnClickListener(v -> dialog.dismiss());
        Button yes = dialog.findViewById(R.id.dialog_confirm);
        if (confirm == 0) {
            yes.setVisibility(View.GONE);
        } else {
            yes.setText(confirm);
            yes.setOnClickListener(v -> {
                dialog.dismiss();
                action.run();
            });
        }
        dialog.show();
    }

    /** The export's result as a toast, then the system share sheet on the saved zip. */
    private void shareDiagnostics(LauncherSession.Export export) {
        Toast.makeText(this, export.message, Toast.LENGTH_LONG).show();
        if (export.saved == null) return;
        Intent send = new Intent(Intent.ACTION_SEND).setType("application/zip")
                .putExtra(Intent.EXTRA_STREAM, export.saved.uri)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        // The share sheet previews the clip by its label: the file name, not the MediaStore row id.
        send.setClipData(ClipData.newUri(getContentResolver(), export.saved.name, export.saved.uri));
        try {
            startActivity(Intent.createChooser(send, getString(R.string.export_diagnostics)));
        } catch (ActivityNotFoundException e) {
            AppLog.e(TAG, "nothing shares " + export.saved.uri, e);
        }
    }

    /** Busy dots pulse at 1.2s, unless the system turned animations off. */
    private void startPulse(View dot) {
        if (pulse != null && pulse.getTarget() == dot) return;
        stopPulse();
        if (!ValueAnimator.areAnimatorsEnabled()) return;
        pulse = ObjectAnimator.ofFloat(dot, View.ALPHA, 1f, 0.3f);
        pulse.setDuration(600);
        pulse.setRepeatMode(ValueAnimator.REVERSE);
        pulse.setRepeatCount(ValueAnimator.INFINITE);
        pulse.start();
    }

    private void stopPulse() {
        if (pulse == null) return;
        Object target = pulse.getTarget();
        pulse.cancel();
        if (target instanceof View) ((View) target).setAlpha(1f);
        pulse = null;
    }

    private void bind(Button button, LauncherScreen.Action action) {
        if (action == null) {
            button.setVisibility(View.GONE);
            return;
        }
        button.setVisibility(View.VISIBLE);
        button.setText(actionText(action));
        button.setOnClickListener(v -> perform(action));
    }

    private void perform(LauncherScreen.Action action) {
        switch (action) {
            case PLAY:
                session.launch(this);
                break;
            case INSTALL_FROM_PLAY:
            case UPDATE_FROM_PLAY:
                open(getString(R.string.url_play));
                break;
            case DOWNLOAD_FROM_PUBLISHER:
                open(getString(R.string.url_publisher));
                break;
            case OPEN_OFFICIAL:
                launchNative();
                break;
            case RETRY:
            default:
                session.recheck(this);
        }
    }

    private void launchNative() {
        Intent intent = getPackageManager().getLaunchIntentForPackage(InstalledGame.PACKAGE);
        if (intent == null) {
            open(getString(R.string.url_play));
            return;
        }
        startActivity(intent);
    }

    private void open(String url) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        } catch (ActivityNotFoundException e) {
            AppLog.e(TAG, "nothing opens " + url, e);
        }
    }

    /** A dimension token in dp, comparable with {@link Configuration#screenWidthDp}. */
    private float dp(int dimen) {
        return getResources().getDimension(dimen) / getResources().getDisplayMetrics().density;
    }

    /** A version as players know it: "1.0.12825" from "1.0.12825-production-android-d9601ded". */
    private String valueOrEmpty(String value) {
        if (value == null || value.isEmpty()) return getString(R.string.empty_value);
        int build = value.indexOf('-');
        return build > 0 ? value.substring(0, build) : value;
    }

    /** The tone's colour and its -subtle fill (design/components/Badge). */
    private static int[] toneColors(LauncherScreen.Tone tone) {
        switch (tone) {
            case OK:
                return new int[] {R.color.bpp_success, R.color.bpp_success_subtle};
            case WARN:
                return new int[] {R.color.bpp_warning, R.color.bpp_warning_subtle};
            case BAD:
                return new int[] {R.color.bpp_danger_text, R.color.bpp_danger_subtle};
            case BUSY:
                return new int[] {R.color.bpp_accent, R.color.bpp_accent_subtle};
            case NEUTRAL:
            default:
                return new int[] {R.color.bpp_text_2, R.color.bpp_bg_hover};
        }
    }

    private static int statusText(LauncherScreen.Status status) {
        switch (status) {
            case CHECKING:
                return R.string.status_checking;
            case PREPARING:
                return R.string.status_preparing;
            case DOWNLOADING:
                return R.string.status_downloading;
            case READY:
                return R.string.status_ready;
            case WAITING:
                return R.string.status_waiting;
            case GAME_UPDATE:
                return R.string.status_game_update;
            case NOT_INSTALLED:
                return R.string.status_not_installed;
            case REPLACE:
                return R.string.status_replace;
            case OFFLINE:
            default:
                return R.string.status_offline;
        }
    }

    private static int actionText(LauncherScreen.Action action) {
        switch (action) {
            case PLAY:
                return R.string.play;
            case INSTALL_FROM_PLAY:
                return R.string.install_from_play;
            case UPDATE_FROM_PLAY:
                return R.string.update_from_play;
            case DOWNLOAD_FROM_PUBLISHER:
                return R.string.download_from_publisher;
            case OPEN_OFFICIAL:
                return R.string.native_launch;
            case RETRY:
            default:
                return R.string.retry;
        }
    }

    private String noteLine(LauncherScreen.Note note, LauncherScreen screen) {
        switch (note) {
            case GAME_NOT_ADAPTED:
                return getString(R.string.game_not_adapted, valueOrEmpty(screen.gameVersion));
            case UPDATE_GAME:
                return getString(R.string.update_game, valueOrEmpty(screen.adaptedVersion));
            default:
                return getString(noteText(note));
        }
    }

    private static int noteText(LauncherScreen.Note note) {
        switch (note) {
            case FROM_CACHE:
                return R.string.release_from_cache;
            case LAST_INSTALL_FAILED:
                return R.string.last_install_failed;
            case RUNTIME_MISSING:
                return R.string.runtime_missing;
            case REPLACE_GAME:
                return R.string.replace_game;
            case RELEASE_UNAVAILABLE:
                return R.string.release_unavailable;
            case DEV_INTEROP:
            default:
                return R.string.dev_interop;
        }
    }
}
