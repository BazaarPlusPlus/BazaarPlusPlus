package com.bazaarplusplus.android.loader;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * {@code assets/bpp-runtime.json}, which release/android-app.ts {@code stageAndroidAppRuntime}
 * writes: the Loader Release this App carries and its nonManagedPayloadDigest (null for a
 * development build).
 */
public final class RuntimeFacts {
    public final String loaderTag;
    public final String nonManagedPayloadDigest;

    RuntimeFacts(String loaderTag, String nonManagedPayloadDigest) {
        this.loaderTag = loaderTag;
        this.nonManagedPayloadDigest = nonManagedPayloadDigest;
    }

    public static RuntimeFacts parse(String text) throws JSONException {
        JSONObject json = new JSONObject(text);
        if (json.getInt("schemaVersion") != 1) throw new JSONException("Unknown bpp-runtime.json schemaVersion");
        String digest = json.isNull("nonManagedPayloadDigest") ? null : json.getString("nonManagedPayloadDigest");
        return new RuntimeFacts(json.getJSONObject("loader").getString("tag"), digest);
    }
}
