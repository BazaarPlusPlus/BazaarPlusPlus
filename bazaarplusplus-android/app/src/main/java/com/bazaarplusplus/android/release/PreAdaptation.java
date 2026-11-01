package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.game.GameCodeHash;

import org.json.JSONException;
import org.json.JSONObject;

import java.security.PublicKey;
import java.util.List;

/**
 * A Pre-Adaptation record (ADR 0012, "预适配 interop 包") at
 * {@code android/adaptations/<il2cpp>_<metadata>/<loader tag>.json}. The record is unsigned; its
 * interop statement, purpose {@code pre-adaptation}, carries the signature, so it serves only
 * Compatibility Mode and only for the game code it names.
 */
public final class PreAdaptation {
    public static final String KIND = "android-adaptation";

    public final PlatformManifest.Game game;
    public final PlatformManifest.Interop interop;
    public final PlatformManifest.Loader loader;

    private PreAdaptation(PlatformManifest.Game game, PlatformManifest.Interop interop, PlatformManifest.Loader loader) {
        this.game = game;
        this.interop = interop;
        this.loader = loader;
    }

    public static PreAdaptation decode(String text, ReleaseOrigin origin) throws MalformedReleaseException {
        JSONObject root;
        try {
            root = new JSONObject(text);
        } catch (JSONException e) {
            throw new MalformedReleaseException("The Pre-Adaptation record is not a JSON object", e);
        }
        if (StrictJson.integer(root, "schemaVersion") != 1) throw new MalformedReleaseException("Unknown schemaVersion");
        if (!KIND.equals(StrictJson.string(root, "kind"))) throw new MalformedReleaseException("Not an " + KIND);
        return new PreAdaptation(PlatformManifest.Game.decode(StrictJson.object(root, "game")),
                PlatformManifest.Interop.decode(StrictJson.object(root, "interop"), origin),
                PlatformManifest.Loader.decode(StrictJson.object(root, "loader")));
    }

    /**
     * Usable only for the installed game's code, the Loader Release this App carries, and with a
     * pre-adaptation statement a trusted key signed.
     */
    public boolean usableFor(GameCodeHash installed, String carriedLoaderTag, List<PublicKey> trusted) {
        return game.hash.equals(installed) && loader.tag.equals(carriedLoaderTag)
                && InteropStatement.verify(interop.file.sha256, installed, InteropStatement.PRE_ADAPTATION,
                        interop.signature, trusted);
    }
}
