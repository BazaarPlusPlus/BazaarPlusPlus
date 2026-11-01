package com.bazaarplusplus.android.launch;

import android.content.Context;

import com.bazaarplusplus.android.BppApplication;
import com.bazaarplusplus.android.DevOverrides;
import com.bazaarplusplus.android.R;
import com.bazaarplusplus.android.diag.AppLog;
import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.game.GameCodeHashCache;
import com.bazaarplusplus.android.game.InstalledGame;
import com.bazaarplusplus.android.loader.RuntimeFacts;
import com.bazaarplusplus.android.loader.RuntimeFiles;
import com.bazaarplusplus.android.payload.ManagedPayloadStaging;
import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.InteropStatement;
import com.bazaarplusplus.android.release.PreAdaptation;
import com.bazaarplusplus.android.release.ReleaseCache;
import com.bazaarplusplus.android.release.ReleaseOrigin;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.security.PublicKey;
import java.util.ArrayList;
import java.util.List;

/**
 * Everything the launcher decides on, gathered off the UI thread: the installed game and its
 * Game Code Hash, the Platform Release Manifest (or its cached copy), a usable Pre-Adaptation,
 * the user's Compatibility Mode choice, and the result of {@link LaunchGate}.
 */
final class LaunchCheck {
    final InstalledGame game;
    final GameCodeHash hash;
    /** Null when this build carries no Android App Runtime. */
    final RuntimeFacts facts;
    final ReleaseOrigin origin;
    final ReleaseCache.ManifestState manifest;
    /** Non-null only when usable for the installed game. */
    final PreAdaptation preAdaptation;
    final LaunchGate.Decision decision;
    final List<PublicKey> trusted;
    final String installedModsVersion;
    final boolean lastModsInstallFailed;
    /** A debug build's Development interop, which skips the manifest; null otherwise. */
    final File devInterop;

    private LaunchCheck(InstalledGame game, GameCodeHash hash, RuntimeFacts facts, ReleaseOrigin origin,
            ReleaseCache.ManifestState manifest, PreAdaptation preAdaptation, LaunchGate.Decision decision,
            List<PublicKey> trusted, String installedModsVersion, boolean lastModsInstallFailed, File devInterop) {
        this.game = game;
        this.hash = hash;
        this.facts = facts;
        this.origin = origin;
        this.manifest = manifest;
        this.preAdaptation = preAdaptation;
        this.decision = decision;
        this.trusted = trusted;
        this.installedModsVersion = installedModsVersion;
        this.lastModsInstallFailed = lastModsInstallFailed;
        this.devInterop = devInterop;
    }

    static LaunchCheck run(Context context) throws IOException {
        String gamePackage = DevOverrides.gamePackage(context);
        InstalledGame game = InstalledGame.find(context.getPackageManager(),
                gamePackage != null ? gamePackage : InstalledGame.PACKAGE);
        RuntimeFacts facts = RuntimeFiles.hasRuntime(context) ? RuntimeFiles.facts(context) : null;
        ReleaseOrigin dev = DevOverrides.origin(context);
        ReleaseOrigin origin = dev != null ? dev : ReleaseOrigin.PUBLIC;
        List<PublicKey> trusted = trustedKeys(context);
        File external = AppFiles.external(context);
        String installedMods = ManagedPayloadStaging.installedVersion(external);
        boolean modsFailed = ManagedPayloadStaging.lastInstallFailed(
                ManagedPayloadStaging.remembered(AppFiles.stagedVersion(context)), installedMods, AppFiles.updates(context));
        if (game == null) {
            LaunchGate.Decision decision = LaunchGate.decide(new LaunchGate.Inputs(null, null, false, false, false, false));
            AppLog.i(BppApplication.TAG, "launch gate: " + decision + " (" + gamePackage(gamePackage) + ")");
            return new LaunchCheck(null, null, facts, origin, null, null, decision, trusted, installedMods, modsFailed, null);
        }

        long started = System.nanoTime();
        GameCodeHash hash = GameCodeHashCache.get(AppFiles.gameCodeHash(context), game.lastUpdateTime, game.apks,
                GameCodeHash::compute);
        AppLog.i(BppApplication.TAG, "game " + game.versionName + " (" + game.versionCode + ") code " + hash + " in "
                + (System.nanoTime() - started) / 1_000_000 + " ms");

        ReleaseCache cache = new ReleaseCache(AppFiles.releaseCache(context, dev != null), origin, Download::http);
        ReleaseCache.ManifestState manifest = cache.manifest();
        AppLog.i(BppApplication.TAG, "manifest " + origin.manifestUrl() + ": " + manifest);

        PreAdaptation usable = null;
        LaunchGate.Adapted adapted = null;
        if (manifest.manifest != null) {
            adapted = new LaunchGate.Adapted(manifest.manifest.game.versionCode, manifest.manifest.game.hash);
            // A Pre-Adaptation matters only for code that differs from the release's.
            if (!hash.equals(adapted.hash) && facts != null) {
                PreAdaptation record = cache.preAdaptation(hash, facts.loaderTag);
                if (record != null && record.usableFor(hash, facts.loaderTag, trusted)) usable = record;
                AppLog.i(BppApplication.TAG, "pre-adaptation for " + hash + "/" + facts.loaderTag + ": "
                        + (record == null ? "none" : usable == null ? "not usable" : "usable"));
            }
        }
        boolean appUpdate = manifest.manifest != null && facts != null
                && facts.needsAppUpdateFor(manifest.manifest.nonManagedPayloadDigest);
        boolean chosen = new CompatibilityChoices(AppFiles.compatibilityChoices(context)).chosen(hash);
        LaunchGate.Decision decision = LaunchGate.decide(new LaunchGate.Inputs(
                new LaunchGate.Installed(game.versionCode, hash), adapted, manifest.noRelease, usable != null, chosen,
                appUpdate));
        AppLog.i(BppApplication.TAG, "launch gate: " + decision + (modsFailed ? ", last plugin install failed" : ""));
        return new LaunchCheck(game, hash, facts, origin, manifest, usable, decision, trusted, installedMods, modsFailed,
                DevOverrides.interop(context));
    }

    private static String gamePackage(String override) {
        return override != null ? override : InstalledGame.PACKAGE;
    }

    /** The release certificate the App carries, plus a debug build's development certificate. */
    static List<PublicKey> trustedKeys(Context context) throws IOException {
        List<PublicKey> keys = new ArrayList<>();
        try (InputStream in = context.getResources().openRawResource(R.raw.bpp_release_cert)) {
            keys.add(InteropStatement.certificateKey(in));
        }
        for (File cert : DevOverrides.trustedCertificates(context)) {
            try (InputStream in = new FileInputStream(cert)) {
                keys.add(InteropStatement.certificateKey(in));
            } catch (IOException e) {
                AppLog.e(BppApplication.TAG, "development certificate " + cert + " is not usable", e);
            }
        }
        return keys;
    }

    /** One line per fact, for the diagnostics export. */
    String describe() {
        StringBuilder out = new StringBuilder();
        out.append("tier: ").append(decision).append('\n');
        out.append("game: ").append(game == null ? "not installed"
                : game.versionName + " (" + game.versionCode + ") lastUpdateTime=" + game.lastUpdateTime
                        + " apks=" + game.apks.size()).append('\n');
        out.append("gameCodeHash: ").append(hash).append('\n');
        out.append("runtime: ").append(facts == null ? "none" : facts.loaderTag + " nonManagedPayloadDigest="
                + facts.nonManagedPayloadDigest).append('\n');
        out.append("origin: ").append(origin.base).append('\n');
        out.append("manifest: ").append(manifest).append('\n');
        if (manifest != null && manifest.manifest != null) {
            out.append("manifest game: ").append(manifest.manifest.game.gameVersion).append(" (")
                    .append(manifest.manifest.game.versionCode).append(") ").append(manifest.manifest.game.hash).append('\n');
        }
        out.append("preAdaptation: ").append(preAdaptation == null ? "none usable"
                : preAdaptation.game.gameVersion + " interop " + preAdaptation.interop.file.sha256).append('\n');
        out.append("installedPlugin: ").append(installedModsVersion).append('\n');
        out.append("lastPluginInstallFailed: ").append(lastModsInstallFailed).append('\n');
        out.append("developmentInterop: ").append(devInterop != null).append('\n');
        return out.toString();
    }
}
