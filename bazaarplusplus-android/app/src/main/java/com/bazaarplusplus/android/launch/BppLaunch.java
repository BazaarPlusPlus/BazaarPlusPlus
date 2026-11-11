package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.diag.AppLog;
import com.bazaarplusplus.android.game.InstalledGame;
import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.io.Sha256;
import com.bazaarplusplus.android.loader.LoaderDeployment;
import com.bazaarplusplus.android.payload.ManagedPayloadStaging;
import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.InteropPackage;
import com.bazaarplusplus.android.release.InteropStatement;
import com.bazaarplusplus.android.release.PlatformManifest;

import java.io.File;
import java.io.IOException;
import java.util.List;

/**
 * One BPP launch after the gate allowed it (ADR 0012): verify the Interop Package and take it from
 * the APK or the network, stage the release's Managed Payload the same way, reset LemonLoader's stale deployment state, prepare the game in
 * this process and write the Launch Record last. Any failure stops the launch. The steps are plain
 * Java over {@link LaunchDirs}; only {@link LaunchHost} reaches Android, so a JVM test drives them.
 */
final class BppLaunch {
    static final String TAG = LauncherActivity.TAG;

    /** What the launcher shows while a step runs. */
    interface Progress {
        void preparing();

        void downloading(boolean interop, long received, long total);
    }

    private final LaunchHost host;
    private final LaunchDirs dirs;

    BppLaunch(LaunchHost host, LaunchDirs dirs) {
        this.host = host;
        this.dirs = dirs;
    }

    void prepare(LaunchInputs in, Progress progress) throws Exception {
        // The gate decides first, before any file or network side effect. NOT_INSTALLED refuses, so
        // past this line the game is installed.
        refuseUnlessAllowed(in.decision);
        // Whether the APK carries a runtime is not a gate fact; the launcher hides Play without one.
        if (!in.runtime) throw new IOException("This build carries no Android App Runtime");
        // One prepared game per process: a second launch would keep the first launch's interop
        // while the Launch Record names this one.
        if (host.gamePrepared()) {
            throw new IOException("The game already ran in this process. Close BazaarPlusPlus in recent apps, then open it again");
        }
        long appVersionCode = host.appVersionCode();
        File external = dirs.external;
        if (!external.isDirectory() && !external.mkdirs()) throw new IOException("Cannot create " + external);
        if (in.devInterop != null) {
            prepareDevelopment(in, appVersionCode);
            return;
        }
        // Past the guard without a Development interop the tier is NORMAL, and LaunchCheck.run decides
        // NORMAL only against the game of a manifest it read, so the manifest is not null.
        PlatformManifest manifest = in.manifest;
        PlatformManifest.Interop interop = manifest.interop;
        String purpose = InteropStatement.releasePurpose(manifest.version);

        // The signature binds the zip to the installed game's code and this launch's purpose;
        // check it before downloading anything.
        if (!InteropStatement.verify(interop.file.sha256, in.hash, purpose, interop.signature, in.trusted)) {
            throw new IOException("The interop statement for " + interop.file.sha256 + " (" + purpose + ") does not verify");
        }
        File interopDir = dirs.interopCache;
        File zip = Download.cachedOrFetch(host.source(interop.file), interop.file.size, interop.file.sha256,
                new File(interopDir, interop.file.sha256 + ".zip"),
                (received, total) -> progress.downloading(true, received, total));
        String stray = InteropPackage.strayEntry(zip);
        if (stray != null) {
            zip.setWritable(true);
            zip.delete();
            throw new IOException("The Interop Package holds " + stray);
        }
        keepOnly(interopDir, zip.getName());
        progress.preparing();

        String release = manifest.version;
        File updates = dirs.updates;
        String staged = null;
        if (ManagedPayloadStaging.compareVersions(in.installedModsVersion, release) < 0) {
            File payloadDir = dirs.managedPayloadCache;
            PlatformManifest.Artifact payload = manifest.managedPayload.file;
            File payloadZip = Download.cachedOrFetch(host.source(payload), payload.size, payload.sha256,
                    new File(payloadDir, payload.sha256 + ".zip"),
                    (received, total) -> progress.downloading(false, received, total));
            keepOnly(payloadDir, payloadZip.getName());
            progress.preparing();
            ManagedPayloadStaging.stage(payloadZip, updates, release);
            staged = release;
            AppLog.i(TAG, "staged the Managed Payload " + release + " over installed " + in.installedModsVersion);
        }
        ManagedPayloadStaging.remember(dirs.stagedVersion, staged);
        List<String> pruned = ManagedPayloadStaging.prune(updates, staged);
        if (!pruned.isEmpty()) AppLog.i(TAG, "deleted Updates/ entries this launch did not stage: " + pruned);

        startGame(appVersionCode, zip, interop.file.sha256);
        LaunchRecord record = new LaunchRecord(host.processId(), host.app(), release,
                new LaunchRecord.Game(InstalledGame.PACKAGE, in.gameVersionName, in.gameVersionCode,
                        in.hash.il2cppSha256, in.hash.metadataSha256),
                new LaunchRecord.Interop(interop.file.sha256, interop.snapshotSha256));
        File written = record.write(external);
        AppLog.i(TAG, "wrote " + written + ": release " + release + " interop " + interop.file.sha256);
    }

    /** Throws unless {@link LaunchGate.Decision#bppLaunch} allows this launch; it reads nothing else. */
    static void refuseUnlessAllowed(LaunchGate.Decision decision) throws IOException {
        if (!decision.bppLaunch()) {
            throw new IOException("No BPP launch in the launch tier " + decision.tier
                    + (decision.developmentInterop ? " with a Development interop" : ""));
        }
    }

    /**
     * A Development interop: no manifest, no staging. ADR 0012's rule for a development deployment
     * applies, so the launch writes no Launch Record and deletes a stale one.
     */
    private void prepareDevelopment(LaunchInputs inputs, long appVersionCode) throws Exception {
        File interop = readOnlyCopy(inputs.devInterop, new File(dirs.interopCache, "development.zip"));
        String sha256 = Sha256.of(interop);
        startGame(appVersionCode, interop, sha256);
        File record = new File(dirs.external, LaunchRecord.PATH);
        if (record.exists() && !record.delete()) throw new IOException("Cannot delete " + record);
        AppLog.i(TAG, "Development interop " + sha256 + ": no Launch Record");
    }

    private void startGame(long appVersionCode, File interop, String interopSha256) throws Exception {
        AppLog.i(TAG, "loader deployment reset: " + LoaderDeployment.apply(dirs.loaderDeployment,
                new File(dirs.external, "MelonLoader"), new LoaderDeployment.Identity(appVersionCode, interopSha256)));
        host.prepareGame(interop);
    }

    private static void keepOnly(File dir, String keep) {
        File[] files = dir.listFiles();
        if (files == null) return;
        for (File file : files) {
            if (file.getName().equals(keep)) continue;
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
