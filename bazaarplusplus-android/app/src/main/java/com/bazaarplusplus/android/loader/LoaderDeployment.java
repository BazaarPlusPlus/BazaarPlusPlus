package com.bazaarplusplus.android.loader;

import com.bazaarplusplus.android.io.AtomicFiles;

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

// LemonLoader keys deployment on the game's lastUpdateTime. App or interop updates leave it unchanged (ADR 0012).
public final class LoaderDeployment {
    static final List<String> RUNTIME_MARKERS = Collections.unmodifiableList(List.of(
            ".lemonloader-deployment-state",
            ".lemonloader-deployment-state.next",
            ".lemonloader-deployment-state.previous",
            ".lemonloader-dotnet-hash",
            ".lemonloader-loader-net6-hash",
            ".lemonloader-loader-dependencies-hash",
            ".lemonloader-interop-hash",
            ".lemonloader-backups",
            "MelonLoader/Il2CppAssemblies",
            // LemonLoader keeps the previously deployed Updater until its file is removed.
            "Plugins/BazaarPlusPlus.Updater.dll"));
    static final List<String> INTEROP_MARKERS = Collections.unmodifiableList(List.of(
            ".lemonloader-interop-hash",
            "MelonLoader/Il2CppAssemblies"));

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

    public static List<String> stalePaths(Identity previous, Identity next) {
        if (previous == null || previous.appVersionCode != next.appVersionCode) return RUNTIME_MARKERS;
        if (!previous.interopSha256.equals(next.interopSha256)) return INTEROP_MARKERS;
        return List.of();
    }

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
        try {
            AtomicFiles.writeUtf8(stateFile, identity.toJson().toString());
        } catch (JSONException e) {
            throw new IOException(e);
        }
    }

    private static void deleteTree(File file) {
        File[] children = Files.isSymbolicLink(file.toPath()) ? null : file.listFiles();
        if (children != null) for (File child : children) deleteTree(child);
        file.delete();
    }
}
