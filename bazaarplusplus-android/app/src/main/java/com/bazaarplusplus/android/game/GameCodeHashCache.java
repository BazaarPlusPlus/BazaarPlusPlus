package com.bazaarplusplus.android.game;

import com.bazaarplusplus.android.io.AtomicFiles;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

/**
 * The Game Code Hash of the installed game, kept in the App's internal storage. Hashing
 * libil2cpp.so and global-metadata.dat reads hundreds of megabytes, so the result is reused
 * while the game's {@code lastUpdateTime} and APK paths stay the same (ADR 0012).
 */
public final class GameCodeHashCache {
    /** Computes the hash when the cache cannot answer. */
    public interface Computer {
        GameCodeHash compute(List<String> apks) throws IOException;
    }

    private GameCodeHashCache() {}

    public static GameCodeHash get(File cache, long lastUpdateTime, List<String> apks, Computer computer)
            throws IOException {
        GameCodeHash cached = read(cache, lastUpdateTime, apks);
        if (cached != null) return cached;
        GameCodeHash computed = computer.compute(apks);
        try {
            JSONArray paths = new JSONArray();
            for (String apk : apks) paths.put(apk);
            AtomicFiles.writeUtf8(cache, new JSONObject()
                    .put("lastUpdateTime", lastUpdateTime)
                    .put("apks", paths)
                    .put("il2cppSha256", computed.il2cppSha256)
                    .put("metadataSha256", computed.metadataSha256)
                    .toString());
        } catch (JSONException e) {
            throw new IOException(e);
        }
        return computed;
    }

    static GameCodeHash read(File cache, long lastUpdateTime, List<String> apks) {
        String text = AtomicFiles.readUtf8(cache);
        if (text == null) return null;
        try {
            JSONObject json = new JSONObject(text);
            if (json.getLong("lastUpdateTime") != lastUpdateTime) return null;
            JSONArray paths = json.getJSONArray("apks");
            List<String> cached = new ArrayList<>();
            for (int i = 0; i < paths.length(); i++) cached.add(paths.getString(i));
            if (!cached.equals(apks)) return null;
            return new GameCodeHash(json.getString("il2cppSha256"), json.getString("metadataSha256"));
        } catch (JSONException | RuntimeException e) {
            return null;
        }
    }
}
