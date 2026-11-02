package com.bazaarplusplus.android.loader;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

import org.junit.Test;

public class RuntimeFactsTest {
    static String facts(String digest) {
        return "{\"schemaVersion\": 1, \"loader\": {\"tag\": \"v0.7.4-android.4\", \"sha256\": \"" + "e".repeat(64)
                + "\"}, \"updaterSha256\": \"" + "f".repeat(64) + "\", \"nonManagedPayloadDigest\": " + digest + "}";
    }

    @Test
    public void aReleaseBuildCarriesItsRuntimeDigest() throws Exception {
        RuntimeFacts facts = RuntimeFacts.parse(facts("\"" + "6".repeat(64) + "\""));
        assertEquals("v0.7.4-android.4", facts.loaderTag);
        assertEquals("6".repeat(64), facts.nonManagedPayloadDigest);
    }

    @Test
    public void aDevelopmentBuildHasNoRuntimeDigest() throws Exception {
        RuntimeFacts facts = RuntimeFacts.parse(facts("null"));
        assertNull(facts.nonManagedPayloadDigest);
    }
}
