package com.bazaarplusplus.android.loader;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Objects;

/**
 * The App's own record of what it last gave LemonLoader (ADR 0012, "Android App 管理 LemonLoader
 * 的部署状态"). LemonLoader keys its deployment revision and its interop marker on the GAME's
 * lastUpdateTime, because getPackageName answers the game's package in the App. So a new App
 * build or a new Interop Package with the same game would be ignored; before each launch the App
 * deletes the LemonLoader markers that its own record says are stale.
 */
public final class LoaderDeployment {
    /** LemonLoader's state under files/MelonLoader/ that a new App build invalidates. */
    static final List<String> RUNTIME_MARKERS = Collections.unmodifiableList(List.of(
            ".lemonloader-deployment-state",
            ".lemonloader-deployment-state.next",
            ".lemonloader-deployment-state.previous",
            ".lemonloader-dotnet-hash",
            ".lemonloader-loader-net6-hash",
            ".lemonloader-loader-dependencies-hash",
            ".lemonloader-interop-hash",
            ".lemonloader-backups",
            // The interop LemonLoader extracted: a new one must not leave the old files behind.
            "MelonLoader/Il2CppAssemblies",
            // The App's packaged deployment: LemonLoader keeps a file it once deployed.
            "Plugins/BazaarPlusPlus.Updater.dll"));
    /** What a new Interop Package with the same App build invalidates. */
    static final List<String> INTEROP_MARKERS = Collections.unmodifiableList(List.of(
            ".lemonloader-interop-hash",
            "MelonLoader/Il2CppAssemblies"));

    /** What one launch gives LemonLoader. */
    public static final class Identity {
        public final long appVersionCode;
        public final String interopSha256;

        public Identity(long appVersionCode, String interopSha256) {
            this.appVersionCode = appVersionCode;
            this.interopSha256 = Objects.requireNonNull(interopSha256);
        }

        JSONObject toJson() throws JSONException {
            return new JSONObject().put("appVersionCode", appVersionCode).put("interopSha256", interopSha256);
        }

        static Identity fromJson(JSONObject json) throws JSONException {
            return new Identity(json.getLong("appVersionCode"), json.getString("interopSha256"));
        }
    }

    private LoaderDeployment() {}

    /**
     * The paths under files/MelonLoader/ to delete before launching with {@code next}. No record
     * (a fresh install, or one the App cannot read) counts as a new App build.
     */
    public static List<String> stalePaths(Identity previous, Identity next) {
        if (previous == null || previous.appVersionCode != next.appVersionCode) return RUNTIME_MARKERS;
        if (!previous.interopSha256.equals(next.interopSha256)) return INTEROP_MARKERS;
        return List.of();
    }

    /** Deletes what {@link #stalePaths} names and records {@code next}; call before every launch. */
    public static List<String> apply(File stateFile, File melonRoot, Identity next) throws IOException {
        Identity previous = read(stateFile);
        List<String> deleted = new ArrayList<>();
        for (String relative : stalePaths(previous, next)) {
            File target = new File(melonRoot, relative);
            if (target.exists()) {
                deleteTree(target);
                if (target.exists()) throw new IOException("Cannot delete " + target);
                deleted.add(relative);
            }
        }
        write(stateFile, next);
        return deleted;
    }

    static Identity read(File stateFile) {
        try {
            return Identity.fromJson(new JSONObject(new String(Files.readAllBytes(stateFile.toPath()),
                    StandardCharsets.UTF_8)));
        } catch (IOException | JSONException | RuntimeException e) {
            return null;
        }
    }

    static void write(File stateFile, Identity identity) throws IOException {
        File tmp = new File(stateFile.getPath() + ".tmp");
        try {
            Files.write(tmp.toPath(), identity.toJson().toString().getBytes(StandardCharsets.UTF_8));
        } catch (JSONException e) {
            throw new IOException(e);
        }
        if (!tmp.renameTo(stateFile)) throw new IOException("Cannot replace " + stateFile);
    }

    private static void deleteTree(File file) {
        File[] children = Files.isSymbolicLink(file.toPath()) ? null : file.listFiles();
        if (children != null) for (File child : children) deleteTree(child);
        file.delete();
    }
}
