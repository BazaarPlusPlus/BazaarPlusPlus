package com.bazaarplusplus.android.loader;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * {@code assets/bpp-runtime.json}, which release/android-app.mjs {@code stageAndroidAppRuntime}
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

    /**
     * ADR 0012: a release whose nonManagedPayloadDigest differs from this App's cannot run here. A
     * development build has no digest and runs any release.
     */
    public boolean needsAppUpdateFor(String releaseDigest) {
        return nonManagedPayloadDigest != null && !nonManagedPayloadDigest.equals(releaseDigest);
    }
}
