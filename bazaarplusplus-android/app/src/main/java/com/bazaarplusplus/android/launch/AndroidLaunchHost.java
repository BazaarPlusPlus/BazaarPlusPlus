package com.bazaarplusplus.android.launch;

import android.content.Context;

import com.bazaarplusplus.android.BuildConfigFacts;
import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.game.InstalledGame;
import com.bazaarplusplus.android.loader.RuntimeFacts;
import com.bazaarplusplus.android.loader.RuntimeFiles;
import com.bazaarplusplus.android.release.CarriedRelease;
import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.io.File;

/** {@link LaunchHost} in the App: the APK's assets, the package manager, {@link GameSession}. */
final class AndroidLaunchHost implements LaunchHost {
    private final Context context;
    private final InstalledGame game;
    /** Null when this build carries no runtime; {@link BppLaunch#prepare} refuses such a launch first. */
    private final RuntimeFacts runtime;

    /** Reads nothing: {@link BppLaunch#prepare} decides when each fact is read. */
    AndroidLaunchHost(Context context, InstalledGame game, RuntimeFacts runtime) {
        this.context = context;
        this.game = game;
        this.runtime = runtime;
    }

    @Override
    public boolean gamePrepared() {
        return GameSession.isPrepared();
    }

    @Override
    public Download.Source source(PlatformManifest.Artifact file) {
        return CarriedRelease.source(context.getAssets(), file);
    }

    @Override
    public long appVersionCode() {
        return BuildConfigFacts.versionCode(context);
    }

    @Override
    public LaunchRecord.App app() {
        return new LaunchRecord.App(context.getPackageName(), BuildConfigFacts.versionName(context),
                BuildConfigFacts.versionCode(context), runtime.nonManagedPayloadDigest);
    }

    @Override
    public void prepareGame(File interopZip) throws Exception {
        long appVersionCode = BuildConfigFacts.versionCode(context);
        GameSession.prepare(context, game, new GameSession.Inputs(RuntimeFiles.bridgeDex(context, appVersionCode),
                RuntimeFiles.runtimeZip(context, appVersionCode), interopZip));
    }

    @Override
    public long processId() {
        return android.os.Process.myPid();
    }
}
