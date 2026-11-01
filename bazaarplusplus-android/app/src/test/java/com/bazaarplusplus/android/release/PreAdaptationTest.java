package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.game.GameCodeHash;

import org.json.JSONObject;
import org.junit.Test;

import java.security.PublicKey;
import java.util.List;

public class PreAdaptationTest {
    static final GameCodeHash GAME = new GameCodeHash("a".repeat(64), "b".repeat(64));
    static final String ZIP = "c".repeat(64);
    static final String TAG = "v0.7.4-android.4";
    static final List<PublicKey> TRUSTED = List.of(InteropStatementTest.KEY.getPublic());

    static String record(GameCodeHash game, String tag, String signature) throws Exception {
        return new JSONObject()
                .put("schemaVersion", 1)
                .put("kind", "android-adaptation")
                .put("game", new JSONObject().put("gameVersion", "1.0.12900-production-android-0123abcd")
                        .put("versionCode", 12900).put("il2cppSha256", game.il2cppSha256)
                        .put("metadataSha256", game.metadataSha256))
                .put("interop", new JSONObject().put("url", ReleaseOrigin.PUBLIC.interopUrl(ZIP)).put("size", 100)
                        .put("sha256", ZIP).put("snapshotSha256", "d".repeat(64)).put("signature", signature))
                .put("loader", new JSONObject().put("tag", tag).put("sha256", "e".repeat(64)))
                .put("il2cppInteropVersion", "1.5.3.0")
                .put("publishedAt", "2026-11-01T00:00:00.000Z")
                .toString();
    }

    @Test
    public void theRecordKeyNamesTheGameCodeAndTheLoaderRelease() {
        assertEquals("https://cdn.bazaarplusplus.com/installer/android/adaptations/" + "a".repeat(64) + "_" + "b".repeat(64)
                + "/v0.7.4-android.4.json", ReleaseOrigin.PUBLIC.preAdaptationUrl(GAME, TAG));
        assertThrows(IllegalArgumentException.class, () -> ReleaseOrigin.PUBLIC.preAdaptationUrl(GAME, "../x"));
    }

    @Test
    public void usableOnlyForItsGameItsLoaderAndAPreAdaptationSignature() throws Exception {
        String signature = InteropStatementTest.sign(InteropStatementTest.KEY, ZIP, GAME, InteropStatement.PRE_ADAPTATION);
        PreAdaptation record = PreAdaptation.decode(record(GAME, TAG, signature), ReleaseOrigin.PUBLIC);
        assertEquals(12900, record.game.versionCode);
        assertTrue(record.usableFor(GAME, TAG, TRUSTED));
        assertFalse("another installed game", record.usableFor(new GameCodeHash("a".repeat(64), "f".repeat(64)), TAG,
                TRUSTED));
        assertFalse("another Loader Release", record.usableFor(GAME, "v0.7.5-android.1", TRUSTED));
        assertFalse("an untrusted signer", record.usableFor(GAME, TAG, List.of(InteropStatementTest.OTHER_KEY.getPublic())));

        String release = InteropStatementTest.sign(InteropStatementTest.KEY, ZIP, GAME, InteropStatement.releasePurpose("6.1.0"));
        assertFalse("a release statement never feeds Compatibility Mode",
                PreAdaptation.decode(record(GAME, TAG, release), ReleaseOrigin.PUBLIC).usableFor(GAME, TAG, TRUSTED));
    }

    @Test
    public void aRecordOfAnotherGameIsNotUsableForTheInstalledOne() throws Exception {
        GameCodeHash other = new GameCodeHash("f".repeat(64), "b".repeat(64));
        String signature = InteropStatementTest.sign(InteropStatementTest.KEY, ZIP, other, InteropStatement.PRE_ADAPTATION);
        assertFalse(PreAdaptation.decode(record(other, TAG, signature), ReleaseOrigin.PUBLIC).usableFor(GAME, TAG, TRUSTED));
    }

    @Test
    public void rejectsAnotherKindOrSchema() throws Exception {
        JSONObject json = new JSONObject(record(GAME, TAG, "x"));
        json.put("kind", "android-app");
        assertThrows(MalformedReleaseException.class, () -> PreAdaptation.decode(json.toString(), ReleaseOrigin.PUBLIC));
        json.put("kind", "android-adaptation").put("schemaVersion", 2);
        assertThrows(MalformedReleaseException.class, () -> PreAdaptation.decode(json.toString(), ReleaseOrigin.PUBLIC));
    }
}
