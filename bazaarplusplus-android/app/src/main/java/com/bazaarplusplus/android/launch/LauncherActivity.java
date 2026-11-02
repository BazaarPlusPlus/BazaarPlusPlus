package com.bazaarplusplus.android.launch;

import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.Insets;
import android.net.Uri;
import android.os.Bundle;
import android.view.View;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;

import com.bazaarplusplus.android.BuildConfigFacts;
import com.bazaarplusplus.android.R;
import com.bazaarplusplus.android.diag.AppLog;
import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.game.InstalledGame;

import java.util.ArrayList;
import java.util.List;

/**
 * The App's start screen (design/components/Launcher). It renders {@link LauncherSession}'s
 * {@link LauncherScreen} into one column, or two equal panes when the window is at least
 * {@code pane-breakpoint} wide, and hands taps back to the session. The Activity handles size and
 * text-scale changes itself (see the manifest), so a fold re-renders the screen
 * without recreating it; the work and its state live in the session either way.
 */
public final class LauncherActivity extends Activity implements LauncherSession.Listener {
    static final String TAG = "BppLauncher";
    /** Android font scale from which the panes would be too narrow for their text. */
    private static final float PANES_MAX_FONT_SCALE = 1.3f;

    private final LauncherSession session = LauncherSession.get();
    private boolean panes;
    private Insets insets = Insets.NONE;
    private ObjectAnimator pulse;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        // Android 15 draws apps edge to edge; do the same on older versions and pad by the insets.
        getWindow().setDecorFitsSystemWindows(false);
        inflate(0);
        session.attach(this);
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
        super.onDestroy();
    }

    @Override
    public void onConfigurationChanged(Configuration config) {
        super.onConfigurationChanged(config);
        ScrollView scroll = findViewById(R.id.scroll);
        inflate(scroll == null ? 0 : scroll.getScrollY());
    }

    @Override
    public void changed() {
        render();
    }

    @Override
    public void startGame() {
        startActivity(new Intent().setClassName(this, GameSession.GAME_ACTIVITY));
        finish();
    }

    /** Picks the layout for the current window and text size, then renders the session into it. */
    private void inflate(int scrollY) {
        Configuration config = getResources().getConfiguration();
        float breakpoint = getResources().getDimension(R.dimen.bpp_pane_breakpoint) / getResources().getDisplayMetrics().density;
        panes = config.screenWidthDp >= breakpoint && config.fontScale < PANES_MAX_FONT_SCALE;
        stopPulse();
        setContentView(panes ? R.layout.launcher_panes : R.layout.launcher);
        View root = findViewById(R.id.scroll);
        root.setOnApplyWindowInsetsListener((view, windowInsets) -> {
            insets = windowInsets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            pad();
            return WindowInsets.CONSUMED;
        });
        root.addOnLayoutChangeListener((view, left, top, right, bottom, oldLeft, oldTop, oldRight, oldBottom) -> {
            if (right - left != oldRight - oldLeft) pad();
        });
        root.requestApplyInsets();
        // The cover's corners follow the card's (android:clipToOutline is API 31).
        findViewById(R.id.card).setClipToOutline(true);
        findViewById(R.id.diagnostics).setOnClickListener(v -> session.exportDiagnostics(this,
                getString(R.string.diagnostics_exported), getString(R.string.diagnostics_failed)));
        ((TextView) findViewById(R.id.info_app)).setText(getString(R.string.version_value,
                BuildConfigFacts.versionName(this)));
        render();
        root.post(() -> root.scrollTo(0, scrollY));
    }

    /** The page gutter, plus whatever centres the content within launcher-max, inside the insets. */
    private void pad() {
        View root = findViewById(R.id.scroll);
        View page = findViewById(R.id.page);
        int gutter = getResources().getDimensionPixelSize(panes ? R.dimen.bpp_space_24 : R.dimen.bpp_space_16);
        int max = getResources().getDimensionPixelSize(R.dimen.bpp_launcher_max);
        int width = root.getWidth() - insets.left - insets.right;
        int centre = width <= 0 ? 0 : Math.max(0, (width - 2 * gutter - max) / 2);
        int bottom = getResources().getDimensionPixelSize(R.dimen.bpp_space_24);
        page.setPadding(insets.left + gutter + centre, insets.top, insets.right + gutter + centre, insets.bottom + bottom);
    }

    private void render() {
        LauncherScreen screen = session.screen();
        if (screen == null) return;
        LaunchCheck check = session.check();

        renderBanners(screen);
        renderCover(check);
        renderStatus(screen);
        renderProgress(screen.progress);

        Button primary = findViewById(R.id.primary);
        bind(primary, screen.primary);
        Button[] secondary = {findViewById(R.id.secondary_1), findViewById(R.id.secondary_2)};
        for (int i = 0; i < secondary.length; i++) {
            bind(secondary[i], i < screen.secondary.size() ? screen.secondary.get(i) : null);
        }

        List<String> notes = new ArrayList<>();
        for (LauncherScreen.Note note : screen.notes) notes.add(getString(noteText(note)));
        TextView notesView = findViewById(R.id.notes);
        notesView.setText(String.join("\n", notes));
        notesView.setVisibility(notes.isEmpty() ? View.GONE : View.VISIBLE);

        ((TextView) findViewById(R.id.info_game)).setText(valueOrEmpty(screen.gameVersion));
        ((TextView) findViewById(R.id.info_adapted)).setText(valueOrEmpty(screen.adaptedVersion));

        Button diagnostics = findViewById(R.id.diagnostics);
        diagnostics.setEnabled(!session.exporting());
        diagnostics.setText(session.exporting() ? R.string.exporting_diagnostics : R.string.export_diagnostics);
        TextView result = findViewById(R.id.diagnostics_result);
        result.setText(session.diagnostics());
        result.setVisibility(session.diagnostics() == null ? View.GONE : View.VISIBLE);
    }

    private void renderBanners(LauncherScreen screen) {
        View update = findViewById(R.id.update_banner);
        if (screen.appUpdate == null) {
            update.setVisibility(View.GONE);
        } else {
            LauncherScreen.AppUpdate app = screen.appUpdate;
            banner(update, R.drawable.bpp_banner_info, R.drawable.bpp_icon_info,
                    getString(R.string.app_update_available, app.version));
            Button action = update.findViewById(R.id.banner_action);
            action.setVisibility(app.url == null ? View.GONE : View.VISIBLE);
            action.setText(R.string.download);
            action.setOnClickListener(v -> open(app.url));
        }
        View error = findViewById(R.id.error_banner);
        if (screen.failure == null) {
            error.setVisibility(View.GONE);
        } else {
            banner(error, R.drawable.bpp_banner_error, R.drawable.bpp_icon_error, getString(
                    screen.failure.launch ? R.string.launch_failed : R.string.check_failed, screen.failure.message));
            error.findViewById(R.id.banner_action).setVisibility(View.GONE);
            error.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_ASSERTIVE);
        }
    }

    private static void banner(View banner, int background, int icon, String message) {
        banner.setVisibility(View.VISIBLE);
        banner.setBackgroundResource(background);
        ((ImageView) banner.findViewById(R.id.banner_icon)).setImageResource(icon);
        ((TextView) banner.findViewById(R.id.banner_message)).setText(message);
    }

    /** The installed official game's own icon; nothing over the network. */
    private void renderCover(LaunchCheck check) {
        ImageView icon = findViewById(R.id.cover_icon);
        if (check == null || check.game == null) {
            icon.setVisibility(View.INVISIBLE);
            return;
        }
        PackageManager pm = getPackageManager();
        icon.setImageDrawable(check.game.info.loadIcon(pm));
        icon.setVisibility(View.VISIBLE);
    }

    private void renderStatus(LauncherScreen screen) {
        View badge = findViewById(R.id.status_badge);
        TextView percent = findViewById(R.id.status_percent);
        if (screen.status == null) {
            badge.setVisibility(View.GONE);
            percent.setVisibility(View.INVISIBLE);
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
        text.setText(statusText(screen.status));
        if (screen.progress != null) {
            percent.setText(getString(R.string.percent, screen.progress.percent()));
            percent.setVisibility(View.VISIBLE);
        } else {
            percent.setVisibility(View.INVISIBLE);
        }
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
            case ENABLE_COMPATIBILITY:
                session.chooseCompatibility(this);
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

    private String valueOrEmpty(String value) {
        return value == null || value.isEmpty() ? getString(R.string.empty_value) : value;
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
            case COMPATIBILITY:
                return R.string.status_compatibility;
            case WAITING:
                return R.string.status_waiting;
            case GAME_UPDATE:
                return R.string.status_game_update;
            case NOT_INSTALLED:
                return R.string.status_not_installed;
            case REPLACE:
                return R.string.status_replace;
            case OFFLINE:
                return R.string.status_offline;
            case NOT_RELEASED:
            default:
                return R.string.status_not_released;
        }
    }

    private static int actionText(LauncherScreen.Action action) {
        switch (action) {
            case PLAY:
                return R.string.play;
            case ENABLE_COMPATIBILITY:
                return R.string.compat_opt_in;
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

    private static int noteText(LauncherScreen.Note note) {
        switch (note) {
            case COMPATIBILITY:
                return R.string.compat_explain;
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
            case NO_RELEASE:
                return R.string.no_release;
            case DEV_INTEROP:
            default:
                return R.string.dev_interop;
        }
    }
}
