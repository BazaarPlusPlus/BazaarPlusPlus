package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.io.AtomicFiles;

import java.io.File;
import java.io.IOException;
import java.util.Objects;

// Adding a field requires a schema bump in the App and the mod and an update to contracts/launch-record.v1.json.
public final class LaunchRecord {
    public static final int SCHEMA_VERSION = 1;
    public static final String PATH = "MelonLoader/UserData/BazaarPlusPlus.launch.json";

    public static final class App {
        final String packageName;
        final String versionName;
        final long versionCode;
        final String nonManagedPayloadDigest;

        public App(String packageName, String versionName, long versionCode, String nonManagedPayloadDigest) {
            this.packageName = Objects.requireNonNull(packageName);
            this.versionName = Objects.requireNonNull(versionName);
            this.versionCode = versionCode;
            this.nonManagedPayloadDigest = nonManagedPayloadDigest;
        }
    }

    public static final class Game {
        final String packageName;
        final String versionName;
        final long versionCode;
        final String il2cppSha256;
        final String metadataSha256;

        public Game(String packageName, String versionName, long versionCode, String il2cppSha256, String metadataSha256) {
            this.packageName = Objects.requireNonNull(packageName);
            this.versionName = Objects.requireNonNull(versionName);
            this.versionCode = versionCode;
            this.il2cppSha256 = Objects.requireNonNull(il2cppSha256);
            this.metadataSha256 = Objects.requireNonNull(metadataSha256);
        }
    }

    public static final class Interop {
        final String sha256;
        final String snapshotSha256;

        public Interop(String sha256, String snapshotSha256) {
            this.sha256 = Objects.requireNonNull(sha256);
            this.snapshotSha256 = Objects.requireNonNull(snapshotSha256);
        }
    }

    final long processId;
    final App app;
    final String release;
    final Game game;
    final Interop interop;

    public LaunchRecord(long processId, App app, String release, Game game, Interop interop) {
        this.processId = processId;
        this.app = Objects.requireNonNull(app);
        this.release = Objects.requireNonNull(release);
        this.game = Objects.requireNonNull(game);
        this.interop = Objects.requireNonNull(interop);
    }

    public String toJson() {
        return new StringBuilder("{\n")
                .append("  \"schemaVersion\": ").append(SCHEMA_VERSION).append(",\n")
                .append("  \"processId\": ").append(processId).append(",\n")
                .append("  \"app\": {\n")
                .append("    \"packageName\": ").append(quote(app.packageName)).append(",\n")
                .append("    \"versionName\": ").append(quote(app.versionName)).append(",\n")
                .append("    \"versionCode\": ").append(app.versionCode).append(",\n")
                .append("    \"nonManagedPayloadDigest\": ").append(quote(app.nonManagedPayloadDigest)).append("\n")
                .append("  },\n")
                .append("  \"release\": ").append(quote(release)).append(",\n")
                .append("  \"game\": {\n")
                .append("    \"packageName\": ").append(quote(game.packageName)).append(",\n")
                .append("    \"versionName\": ").append(quote(game.versionName)).append(",\n")
                .append("    \"versionCode\": ").append(game.versionCode).append(",\n")
                .append("    \"il2cppSha256\": ").append(quote(game.il2cppSha256)).append(",\n")
                .append("    \"metadataSha256\": ").append(quote(game.metadataSha256)).append("\n")
                .append("  },\n")
                .append("  \"interop\": {\n")
                .append("    \"sha256\": ").append(quote(interop.sha256)).append(",\n")
                .append("    \"snapshotSha256\": ").append(quote(interop.snapshotSha256)).append("\n")
                .append("  }\n")
                .append("}\n")
                .toString();
    }

    public File write(File externalFiles) throws IOException {
        File target = new File(externalFiles, PATH);
        AtomicFiles.writeUtf8(target, toJson());
        return target;
    }

    static String quote(String value) {
        if (value == null) return "null";
        StringBuilder out = new StringBuilder(value.length() + 2).append('"');
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            switch (c) {
                case '"': out.append("\\\""); break;
                case '\\': out.append("\\\\"); break;
                case '\b': out.append("\\b"); break;
                case '\f': out.append("\\f"); break;
                case '\n': out.append("\\n"); break;
                case '\r': out.append("\\r"); break;
                case '\t': out.append("\\t"); break;
                default:
                    if (c < 0x20) {
                        out.append(String.format(java.util.Locale.ROOT, "\\u%04x", (int) c));
                    } else {
                        out.append(c);
                    }
            }
        }
        return out.append('"').toString();
    }
}
