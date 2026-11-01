package com.bazaarplusplus.android.release;

import org.json.JSONObject;

import java.util.regex.Pattern;

/** Typed reads that reject a wrong JSON type instead of coercing it, as org.json's getters do. */
final class StrictJson {
    static final Pattern SHA256 = Pattern.compile("[0-9a-f]{64}");
    static final Pattern VERSION = Pattern.compile("(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)");

    private StrictJson() {}

    static JSONObject object(JSONObject parent, String key) throws MalformedReleaseException {
        Object value = parent.opt(key);
        if (!(value instanceof JSONObject)) throw new MalformedReleaseException(key + " is not an object");
        return (JSONObject) value;
    }

    static String string(JSONObject parent, String key) throws MalformedReleaseException {
        Object value = parent.opt(key);
        if (!(value instanceof String) || ((String) value).isEmpty()) {
            throw new MalformedReleaseException(key + " is not a non-empty string");
        }
        return (String) value;
    }

    static String sha256(JSONObject parent, String key) throws MalformedReleaseException {
        String value = string(parent, key);
        if (!SHA256.matcher(value).matches()) throw new MalformedReleaseException(key + " is not a sha256");
        return value;
    }

    static long positive(JSONObject parent, String key) throws MalformedReleaseException {
        long value = integer(parent, key);
        if (value <= 0) throw new MalformedReleaseException(key + " is not positive");
        return value;
    }

    static long integer(JSONObject parent, String key) throws MalformedReleaseException {
        Object value = parent.opt(key);
        if (!(value instanceof Integer || value instanceof Long)) {
            throw new MalformedReleaseException(key + " is not an integer");
        }
        return ((Number) value).longValue();
    }

    static boolean bool(JSONObject parent, String key) throws MalformedReleaseException {
        Object value = parent.opt(key);
        if (!(value instanceof Boolean)) throw new MalformedReleaseException(key + " is not a boolean");
        return (Boolean) value;
    }

    static String version(JSONObject parent, String key) throws MalformedReleaseException {
        String value = string(parent, key);
        if (!VERSION.matcher(value).matches()) throw new MalformedReleaseException(key + " is not a x.y.z version");
        return value;
    }
}
