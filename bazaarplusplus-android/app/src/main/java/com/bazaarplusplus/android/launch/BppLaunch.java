package com.bazaarplusplus.android.launch;

import android.content.Context;

import com.bazaarplusplus.android.BuildConfigFacts;
import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.diag.AppLog;
import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.game.InstalledGame;
import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.loader.LoaderDeployment;
import com.bazaarplusplus.android.loader.RuntimeFiles;
import com.bazaarplusplus.android.payload.ManagedPayloadStaging;
import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.InteropPackage;
import com.bazaarplusplus.android.release.InteropStatement;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * One BPP launch after the gate allowed it (ADR 0012): verify and fetch the Interop Package, stage
 * the release's Managed Payload, reset LemonLoader's stale deployment state, prepare the game in
 * this process and write the Launch Record last. Any failure stops the launch.
 */
final class BppLaunch {
    static final String TAG = LauncherActivity.TAG;

    /** What the launcher shows while a step runs. */
    interface Progress {
        void preparing();

        void downloading(boolean interop, long received, long total);
    }

    private BppLaunch() {}

    static void prepare(Context context, LaunchCheck check, Progress progress) throws Exception {
        if (check.game == null) throw new IOException("The official game is not installed");
        if (check.facts == null) throw new IOException("This build carries no Android App Runtime");
        // One prepared game per process: a second launch would keep the first launch's interop
        // while the Launch Record names this one.
        if (GameSession.isPrepared()) {
            throw new IOException("The game already ran in this process. Close BazaarPlusPlus in recent apps, then open it again");
        }
        long appVersionCode = BuildConfigFacts.versionCode(context);
        File external = AppFiles.external(context);
        if (check.devInterop != null) {
            prepareDevelopment(context, check, appVersionCode, external);
            return;
        }
        PlatformManifest manifest = check.manifest.manifest;
        boolean compatibility = check.decision.compatibility();
        PlatformManifest.Interop interop = compatibility ? check.preAdaptation.interop : manifest.interop;
        String purpose = compatibility ? InteropStatement.PRE_ADAPTATION : InteropStatement.releasePurpose(manifest.version);

        // The signature binds the zip to the installed game's code and this launch's purpose;
        // check it before downloading anything.
        if (!InteropStatement.verify(interop.file.sha256, check.hash, purpose, interop.signature, check.trusted)) {
            throw new IOException("The interop statement for " + interop.file.sha256 + " (" + purpose + ") does not verify");
        }
        File interopDir = AppFiles.interopCache(context);
        File zip = Download.cachedOrFetch(Download.http(interop.file.url), interop.file.size, interop.file.sha256,
                new File(interopDir, interop.file.sha256 + ".zip"),
                (received, total) -> progress.downloading(true, received, total));
        String stray = InteropPackage.strayEntry(zip);
        if (stray != null) {
            zip.setWritable(true);
            zip.delete();
            throw new IOException("The Interop Package holds " + stray);
        }
        keepOnly(interopDir, zip.getName(), manifest.interop.file.sha256 + ".zip");
        progress.preparing();

        String release = manifest.version;
        File updates = AppFiles.updates(context);
        String staged = null;
        if (ManagedPayloadStaging.compareVersions(check.installedModsVersion, release) < 0) {
            File payloadDir = AppFiles.managedPayloadCache(context);
            PlatformManifest.Artifact payload = manifest.managedPayload.file;
            File payloadZip = Download.cachedOrFetch(Download.http(payload.url), payload.size, payload.sha256,
                    new File(payloadDir, payload.sha256 + ".zip"),
                    (received, total) -> progress.downloading(false, received, total));
            keepOnly(payloadDir, payloadZip.getName());
            progress.preparing();
            ManagedPayloadStaging.stage(payloadZip, updates, release);
            staged = release;
            AppLog.i(TAG, "staged the Managed Payload " + release + " over installed " + check.installedModsVersion);
        }
        ManagedPayloadStaging.remember(AppFiles.stagedVersion(context), staged);
        List<String> pruned = ManagedPayloadStaging.prune(updates, staged);
        if (!pruned.isEmpty()) AppLog.i(TAG, "deleted Updates/ entries this launch did not stage: " + pruned);

        startGame(context, check, appVersionCode, external, zip, interop.file.sha256);
        LaunchRecord record = new LaunchRecord(android.os.Process.myPid(),
                new LaunchRecord.App(context.getPackageName(), BuildConfigFacts.versionName(context), appVersionCode,
                        check.facts.nonManagedPayloadDigest),
                release, compatibility ? LaunchRecord.MODE_COMPATIBILITY : LaunchRecord.MODE_NORMAL,
                new LaunchRecord.Game(InstalledGame.PACKAGE, check.game.versionName, check.game.versionCode,
                        check.hash.il2cppSha256, check.hash.metadataSha256),
                new LaunchRecord.Interop(interop.file.sha256, interop.snapshotSha256,
                        compatibility ? LaunchRecord.SOURCE_PRE_ADAPTATION : LaunchRecord.SOURCE_RELEASE));
        File written = record.write(external);
        AppLog.i(TAG, "wrote " + written + ": " + record.mode + " release " + release + " interop " + interop.file.sha256);
    }

    /**
     * A Development interop: no manifest, no staging. ADR 0012's rule for a development deployment
     * applies, so the launch writes no Launch Record and deletes a stale one.
     */
    private static void prepareDevelopment(Context context, LaunchCheck check, long appVersionCode, File external)
            throws Exception {
        File interop = readOnlyCopy(check.devInterop, new File(AppFiles.interopCache(context), "development.zip"));
        String sha256;
        try (InputStream in = new FileInputStream(interop)) {
            sha256 = GameCodeHash.Sha256.of(in);
        }
        startGame(context, check, appVersionCode, external, interop, sha256);
        File record = new File(external, LaunchRecord.PATH);
        if (record.exists() && !record.delete()) throw new IOException("Cannot delete " + record);
        AppLog.i(TAG, "Development interop " + sha256 + ": no Launch Record");
    }

    private static void startGame(Context context, LaunchCheck check, long appVersionCode, File external, File interop,
            String interopSha256) throws Exception {
        AppLog.i(TAG, "loader deployment reset: " + LoaderDeployment.apply(AppFiles.loaderDeployment(context),
                new File(external, "MelonLoader"), new LoaderDeployment.Identity(appVersionCode, interopSha256)));
        GameSession.prepare(context, check.game, new GameSession.Inputs(RuntimeFiles.bridgeDex(context, appVersionCode),
                RuntimeFiles.runtimeZip(context, appVersionCode), interop));
    }

    private static void keepOnly(File dir, String... keep) {
        File[] files = dir.listFiles();
        if (files == null) return;
        Set<String> names = new HashSet<>(Arrays.asList(keep));
        for (File file : files) {
            if (names.contains(file.getName())) continue;
            file.setWritable(true);
            AtomicFiles.deleteTree(file);
        }
    }

    /** A read-only copy in internal storage: a loader must not see the file change under it. */
    static File readOnlyCopy(File source, File target) throws IOException {
        if (target.isFile() && target.length() == source.length() && target.lastModified() >= source.lastModified()) {
            return target;
        }
        File parent = target.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) throw new IOException("Cannot create " + parent);
        File tmp = new File(target.getPath() + ".tmp");
        java.nio.file.Files.copy(source.toPath(), tmp.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING);
        target.setWritable(true);
        target.delete();
        if (!tmp.renameTo(target) || !target.setWritable(false, false)) throw new IOException("Cannot install " + target);
        return target;
    }
}
