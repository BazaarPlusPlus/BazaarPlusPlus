package com.bazaarplusplus.android.diag;

import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Removes account data from text before a diagnostics export leaves the device: email addresses,
 * bearer and JWT tokens, the values of token, session, password and account/profile id fields,
 * UUIDs (the game's account and profile ids) and Steam ids. It over-redacts rather than miss one;
 * sha256 digests, versions and paths stay readable.
 */
public final class Redactor {
    static final String MASK = "<redacted>";

    private static final Pattern EMAIL = Pattern.compile("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}");
    private static final Pattern AUTH_SCHEME = Pattern.compile("(?i)\\b(bearer|basic)\\s+[A-Za-z0-9._~+/=-]+");
    private static final Pattern JWT = Pattern.compile("\\beyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+(\\.[A-Za-z0-9_-]*)?");
    /** A field whose name says it holds a secret or an account id, as key=value, key: value or "key": "value". */
    private static final Pattern SECRET_FIELD = Pattern.compile("(?i)(\"?[\\w.-]*(?:token|session|password|passwd|secret"
            + "|auth|cookie|ticket|credential|api_?key|(?:account|profile|user|player|steam|device|login)_?id)[\\w.-]*\"?"
            + "\\s*[:=]\\s*)(\"[^\"]*\"|[^\\s,;&}\\]\"]+)");
    private static final Pattern UUID = Pattern.compile(
            "\\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\b");
    private static final Pattern STEAM_ID = Pattern.compile("\\b7656119\\d{10}\\b");

    private Redactor() {}

    public static String redact(String text) {
        // Schemes first: "Authorization: Bearer x" must lose x, not only the word Bearer.
        String out = AUTH_SCHEME.matcher(text).replaceAll("$1 " + MASK);
        out = JWT.matcher(out).replaceAll(MASK);
        Matcher field = SECRET_FIELD.matcher(out);
        StringBuffer fields = new StringBuffer();
        while (field.find()) {
            String value = field.group(2);
            String masked = value.startsWith("\"") ? "\"" + MASK + "\"" : MASK;
            field.appendReplacement(fields, Matcher.quoteReplacement(field.group(1) + masked));
        }
        field.appendTail(fields);
        out = EMAIL.matcher(fields.toString()).replaceAll(MASK);
        out = UUID.matcher(out).replaceAll(MASK);
        return STEAM_ID.matcher(out).replaceAll(MASK);
    }
}
