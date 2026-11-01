package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;

import org.json.JSONObject;
import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

public class LaunchRecordTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    /** The shared golden the mod reads (root AGENTS.md, Contracts); unit tests run in app/. */
    static final File GOLDEN = new File("../contracts/launch-record.v1.json");

    static LaunchRecord fromJson(JSONObject json) throws Exception {
        JSONObject app = json.getJSONObject("app");
        JSONObject game = json.getJSONObject("game");
        JSONObject interop = json.getJSONObject("interop");
        return new LaunchRecord(json.getLong("processId"),
                new LaunchRecord.App(app.getString("packageName"), app.getString("versionName"), app.getLong("versionCode"),
                        app.isNull("nonManagedPayloadDigest") ? null : app.getString("nonManagedPayloadDigest")),
                json.getString("release"), json.getString("mode"),
                new LaunchRecord.Game(game.getString("packageName"), game.getString("versionName"),
                        game.getLong("versionCode"), game.getString("il2cppSha256"), game.getString("metadataSha256")),
                new LaunchRecord.Interop(interop.getString("sha256"), interop.getString("snapshotSha256"),
                        interop.getString("source")));
    }

    @Test
    public void reproducesTheSharedGoldenByteForByte() throws Exception {
        String golden = new String(Files.readAllBytes(GOLDEN.toPath()), StandardCharsets.UTF_8);
        assertEquals(golden, fromJson(new JSONObject(golden)).toJson());
    }

    @Test
    public void writesTheAdrsFieldsInOrder() throws Exception {
        LaunchRecord record = new LaunchRecord(4242,
                new LaunchRecord.App("com.bazaarplusplus.android.dev", "0.0.0-dev", 1, null), "6.1.0",
                LaunchRecord.MODE_COMPATIBILITY,
                new LaunchRecord.Game("com.tempogames.thebazaar", "1.0.12900", 12900, "a".repeat(64), "b".repeat(64)),
                new LaunchRecord.Interop("c".repeat(64), "d".repeat(64), LaunchRecord.SOURCE_PRE_ADAPTATION));
        assertEquals("{\n"
                + "  \"schemaVersion\": 1,\n"
                + "  \"processId\": 4242,\n"
                + "  \"app\": {\n"
                + "    \"packageName\": \"com.bazaarplusplus.android.dev\",\n"
                + "    \"versionName\": \"0.0.0-dev\",\n"
                + "    \"versionCode\": 1,\n"
                + "    \"nonManagedPayloadDigest\": null\n"
                + "  },\n"
                + "  \"release\": \"6.1.0\",\n"
                + "  \"mode\": \"compatibility\",\n"
                + "  \"game\": {\n"
                + "    \"packageName\": \"com.tempogames.thebazaar\",\n"
                + "    \"versionName\": \"1.0.12900\",\n"
                + "    \"versionCode\": 12900,\n"
                + "    \"il2cppSha256\": \"" + "a".repeat(64) + "\",\n"
                + "    \"metadataSha256\": \"" + "b".repeat(64) + "\"\n"
                + "  },\n"
                + "  \"interop\": {\n"
                + "    \"sha256\": \"" + "c".repeat(64) + "\",\n"
                + "    \"snapshotSha256\": \"" + "d".repeat(64) + "\",\n"
                + "    \"source\": \"pre-adaptation\"\n"
                + "  }\n"
                + "}\n", record.toJson());

        File external = tmp.newFolder("files");
        File written = record.write(external);
        assertEquals(new File(external, "MelonLoader/UserData/BazaarPlusPlus.launch.json"), written);
        assertEquals(record.toJson(), new String(Files.readAllBytes(written.toPath()), StandardCharsets.UTF_8));
        assertFalse(new File(written.getPath() + ".tmp").exists());
    }

    @Test
    public void quotesAsJsonStringifyDoes() {
        assertEquals("\"a\\\"b\\\\c\\n\\u0001é\"", LaunchRecord.quote("a\"b\\c\n\u0001é"));
    }

    @Test
    public void rejectsAnUnknownModeOrSource() {
        LaunchRecord.App app = new LaunchRecord.App("p", "v", 1, null);
        LaunchRecord.Game game = new LaunchRecord.Game("g", "v", 1, "a", "b");
        assertThrows(IllegalArgumentException.class, () -> new LaunchRecord.Interop("c", "d", "device"));
        assertThrows(IllegalArgumentException.class, () -> new LaunchRecord(1, app, "6.1.0", "safe", game,
                new LaunchRecord.Interop("c", "d", LaunchRecord.SOURCE_RELEASE)));
    }
}
