package com.bazaarplusplus.android.diag;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public class RedactorTest {
    static void assertGone(String secret, String text) {
        String redacted = Redactor.redact(text);
        assertFalse(redacted, redacted.contains(secret));
        assertTrue(redacted, redacted.contains(Redactor.MASK));
    }

    @Test
    public void removesEmails() {
        assertGone("player@example.com", "[12:00] signed in as player@example.com ok");
    }

    @Test
    public void removesBearerAndJwtTokens() {
        assertGone("abc.def-123", "Authorization: Bearer abc.def-123");
        assertGone("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln", "jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln end");
    }

    @Test
    public void removesSecretFieldsInEveryForm() {
        assertGone("s3cr3t", "{\"accessToken\": \"s3cr3t\", \"x\": 1}");
        assertGone("s3cr3t", "session_id=s3cr3t&page=2");
        assertGone("s3cr3t", "refreshToken: s3cr3t");
        assertGone("s3cr3t", "password=s3cr3t");
        assertGone("1234567", "\"accountId\":\"1234567\"");
        assertGone("1234567", "profile_id=1234567");
        assertGone("1234567", "PlayerId: 1234567,");
    }

    @Test
    public void removesUuidsAndSteamIds() {
        assertGone("0f8fad5b-d9cb-469f-a165-70867728950e", "account 0f8fad5b-d9cb-469f-a165-70867728950e loaded");
        assertGone("76561198012345678", "steam 76561198012345678");
    }

    @Test
    public void keepsWhatDiagnosticsNeed() {
        String line = "interop sha256 5777e4d7d4a39579edb3cceb0541a7a08f3bf02d0521f7405a0064189c365fb9 "
                + "game 1.0.12825 (12825) processId 4242 /storage/emulated/0/Android/data/x/files";
        assertEquals(line, Redactor.redact(line));
    }
}
