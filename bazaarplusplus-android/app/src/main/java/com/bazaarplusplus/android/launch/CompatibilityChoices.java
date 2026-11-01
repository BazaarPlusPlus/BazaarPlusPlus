package com.bazaarplusplus.android.launch;

import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.io.AtomicFiles;

import org.json.JSONArray;
import org.json.JSONException;

import java.io.File;
import java.io.IOException;
import java.util.LinkedHashSet;
import java.util.Set;

/**
 * The Game Code Hashes the user opted in to Compatibility Mode for (ADR 0012: the choice is kept
 * per Game Code Hash, not per game version), in the App's internal storage.
 */
public final class CompatibilityChoices {
    private final File file;

    public CompatibilityChoices(File file) {
        this.file = file;
    }

    public boolean chosen(GameCodeHash game) {
        return read().contains(game.toString());
    }

    public void choose(GameCodeHash game) throws IOException {
        Set<String> chosen = read();
        if (!chosen.add(game.toString())) return;
        JSONArray json = new JSONArray();
        for (String hash : chosen) json.put(hash);
        AtomicFiles.writeUtf8(file, json.toString());
    }

    private Set<String> read() {
        Set<String> chosen = new LinkedHashSet<>();
        String text = AtomicFiles.readUtf8(file);
        if (text == null) return chosen;
        try {
            JSONArray json = new JSONArray(text);
            for (int i = 0; i < json.length(); i++) chosen.add(json.getString(i));
        } catch (JSONException e) {
            // An unreadable record counts as no choice: the user is asked again.
        }
        return chosen;
    }
}
