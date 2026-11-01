package com.bazaarplusplus.android.loader;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public class RuntimeFactsTest {
    static String facts(String digest) {
        return "{\"schemaVersion\": 1, \"loader\": {\"tag\": \"v0.7.4-android.4\", \"sha256\": \"" + "e".repeat(64)
                + "\"}, \"updaterSha256\": \"" + "f".repeat(64) + "\", \"nonManagedPayloadDigest\": " + digest + "}";
    }

    @Test
    public void aReleaseBuildNeedsTheReleasesRuntime() throws Exception {
        RuntimeFacts facts = RuntimeFacts.parse(facts("\"" + "6".repeat(64) + "\""));
        assertEquals("v0.7.4-android.4", facts.loaderTag);
        assertFalse(facts.needsAppUpdateFor("6".repeat(64)));
        assertTrue(facts.needsAppUpdateFor("7".repeat(64)));
    }

    @Test
    public void aDevelopmentBuildRunsAnyRelease() throws Exception {
        RuntimeFacts facts = RuntimeFacts.parse(facts("null"));
        assertNull(facts.nonManagedPayloadDigest);
        assertFalse(facts.needsAppUpdateFor("7".repeat(64)));
    }
}
