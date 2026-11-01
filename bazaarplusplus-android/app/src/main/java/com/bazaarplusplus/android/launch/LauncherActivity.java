package com.bazaarplusplus.android.launch;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;
import android.widget.Button;
import android.widget.TextView;

import com.bazaarplusplus.android.BuildConfigFacts;
import com.bazaarplusplus.android.R;
import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.game.InstalledGame;
import com.bazaarplusplus.android.loader.LoaderDeployment;
import com.bazaarplusplus.android.loader.RuntimeFiles;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;

/**
 * The App's start screen: BPP launch (the installed game in this process, with LemonLoader) and
 * native launch (the official App, untouched).
 */
public final class LauncherActivity extends Activity {
    static final String TAG = "BppLauncher";
    /**
     * Where a development build takes an Interop Package that `just android::push-interop` puts in
     * the App's internal storage with run-as; external storage that adb creates is not the App's.
     */
    static final String DEV_INTEROP = "dev/interop.zip";

    private TextView status;
    private Button bppLaunch;
    private Button nativeLaunch;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.launcher);
        status = findViewById(R.id.status);
        bppLaunch = findViewById(R.id.bpp_launch);
        nativeLaunch = findViewById(R.id.native_launch);
        ((TextView) findViewById(R.id.version)).setText(getString(R.string.app_version,
                BuildConfigFacts.versionName(this)));
        bppLaunch.setOnClickListener(v -> launchBpp());
        nativeLaunch.setOnClickListener(v -> launchNative());
    }

    @Override
    protected void onResume() {
        super.onResume();
        InstalledGame game = InstalledGame.find(getPackageManager());
        if (game == null) {
            status.setText(R.string.game_missing);
            bppLaunch.setEnabled(false);
            nativeLaunch.setText(R.string.install_game);
            nativeLaunch.setOnClickListener(v -> openStore());
            return;
        }
        nativeLaunch.setText(R.string.native_launch);
        nativeLaunch.setOnClickListener(v -> launchNative());
        boolean ready = RuntimeFiles.hasRuntime(this) && devInterop().isFile();
        bppLaunch.setEnabled(ready);
        status.setText(ready ? getString(R.string.game_found, game.versionName) : getString(R.string.runtime_missing));
    }

    private File devInterop() {
        return new File(getFilesDir(), DEV_INTEROP);
    }

    private void launchBpp() {
        bppLaunch.setEnabled(false);
        nativeLaunch.setEnabled(false);
        status.setText(R.string.preparing);
        new Thread(() -> {
            try {
                InstalledGame game = InstalledGame.find(getPackageManager());
                if (game == null) throw new IOException("The official game is not installed");
                long versionCode = BuildConfigFacts.versionCode(this);
                File interop = readOnlyCopy(devInterop(), new File(getNoBackupFilesDir(), "interop.zip"));
                String interopSha256;
                try (InputStream in = new FileInputStream(interop)) {
                    interopSha256 = com.bazaarplusplus.android.game.GameCodeHash.Sha256.of(in);
                }
                File melonRoot = new File(getExternalFilesDir(null), "MelonLoader");
                Log.i(TAG, "loader deployment reset: " + LoaderDeployment.apply(
                        new File(getNoBackupFilesDir(), "loader-deployment.json"), melonRoot,
                        new LoaderDeployment.Identity(versionCode, interopSha256)));
                GameSession.prepare(this, game, new GameSession.Inputs(RuntimeFiles.bridgeDex(this, versionCode),
                        RuntimeFiles.runtimeZip(this, versionCode), interop));
                runOnUiThread(() -> {
                    Intent intent = new Intent();
                    intent.setClassName(this, GameSession.GAME_ACTIVITY);
                    startActivity(intent);
                    finish();
                });
            } catch (Throwable t) {
                Log.e(TAG, "BPP launch failed", t);
                runOnUiThread(() -> {
                    status.setText(getString(R.string.launch_failed, String.valueOf(t.getMessage())));
                    nativeLaunch.setEnabled(true);
                });
            }
        }, "BppLaunch").start();
    }

    private void launchNative() {
        Intent intent = getPackageManager().getLaunchIntentForPackage(InstalledGame.PACKAGE);
        if (intent == null) {
            openStore();
            return;
        }
        startActivity(intent);
    }

    private void openStore() {
        startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=" + InstalledGame.PACKAGE))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
    }

    /** A read-only copy in internal storage: external storage may change under a loader. */
    static File readOnlyCopy(File source, File target) throws IOException {
        if (target.isFile() && target.length() == source.length() && target.lastModified() >= source.lastModified()) {
            return target;
        }
        File tmp = new File(target.getPath() + ".tmp");
        java.nio.file.Files.copy(source.toPath(), tmp.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING);
        target.setWritable(true);
        target.delete();
        if (!tmp.renameTo(target) || !target.setWritable(false, false)) throw new IOException("Cannot install " + target);
        return target;
    }
}
