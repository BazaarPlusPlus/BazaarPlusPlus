package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

import android.content.ContextWrapper;

import com.bazaarplusplus.android.launch.LaunchGate.Tier;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.List;
import java.util.stream.Collectors;
import java.util.stream.Stream;

/**
 * {@link BppLaunch#prepare}'s gate guard, and {@link BppLaunch#readOnlyCopy}, the copy a Development
 * interop loads from, on a real file system.
 */
public class BppLaunchTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static final byte[] INTEROP = "development interop".getBytes(StandardCharsets.UTF_8);
    /** Other bytes of the same length. */
    static final byte[] STALE = "DEVELOPMENT INTEROP".getBytes(StandardCharsets.UTF_8);
    static final long SOURCE_TIME = 1_700_000_000_000L;

    File source() throws Exception {
        File source = new File(tmp.getRoot(), "dev/interop.zip");
        assertTrue(source.getParentFile().mkdirs());
        Files.write(source.toPath(), INTEROP);
        assertTrue(source.setLastModified(SOURCE_TIME));
        return source;
    }

    File target() {
        return new File(tmp.getRoot(), "cache/interop/development.zip");
    }

    File tmpOf(File target) {
        return new File(target.getPath() + ".tmp");
    }

    /** A copy put in place by hand, holding {@code bytes} with the modification time {@code time}. */
    File existing(byte[] bytes, long time) throws Exception {
        File target = target();
        assertTrue(target.getParentFile().mkdirs());
        Files.write(target.toPath(), bytes);
        assertTrue(target.setLastModified(time));
        return target;
    }

    byte[] read(File file) throws Exception {
        return Files.readAllBytes(file.toPath());
    }

    @Test
    public void aFreshCopyIsByteEqualAndReadOnly() throws Exception {
        File source = source();
        File copy = BppLaunch.readOnlyCopy(source, target());
        assertEquals(target(), copy);
        assertArrayEquals(INTEROP, read(copy));
        assertFalse(copy.canWrite());
        assertFalse(tmpOf(copy).exists());
        assertTrue("the source stays as it was", source.canWrite());
        assertArrayEquals(INTEROP, read(source));
    }

    @Test
    public void aCopyOfTheSameLengthAndNoOlderThanTheSourceIsReusedUnread() throws Exception {
        File source = source();
        File target = existing(STALE, SOURCE_TIME);
        assertEquals(target, BppLaunch.readOnlyCopy(source, target));
        assertArrayEquals("same length and modification time: the bytes are not compared", STALE, read(target));
        assertTrue("a reused copy is not made read-only", target.canWrite());

        assertTrue(target.setLastModified(SOURCE_TIME + 60_000));
        BppLaunch.readOnlyCopy(source, target);
        assertArrayEquals("a copy newer than the source is reused too", STALE, read(target));
    }

    @Test
    public void aNewerSourceReplacesACopyOfTheSameLength() throws Exception {
        File source = source();
        File target = existing(STALE, SOURCE_TIME - 60_000);
        assertTrue(target.setWritable(false, false));
        BppLaunch.readOnlyCopy(source, target);
        assertArrayEquals(INTEROP, read(target));
        assertFalse(target.canWrite());
    }

    @Test
    public void aSourceOfAnotherLengthReplacesANewerCopy() throws Exception {
        File source = source();
        File target = existing("an older and longer interop".getBytes(StandardCharsets.UTF_8), SOURCE_TIME + 60_000);
        BppLaunch.readOnlyCopy(source, target);
        assertArrayEquals(INTEROP, read(target));
        assertFalse(target.canWrite());
    }

    @Test
    public void aLeftoverTmpIsOverwrittenByACopy() throws Exception {
        File source = source();
        File target = target();
        assertTrue(target.getParentFile().mkdirs());
        Files.write(tmpOf(target).toPath(), "a partial copy from a launch that stopped".getBytes(StandardCharsets.UTF_8));
        BppLaunch.readOnlyCopy(source, target);
        assertArrayEquals(INTEROP, read(target));
        assertFalse("the tmp file became the copy", tmpOf(target).exists());
    }

    @Test
    public void aLeftoverTmpStaysWhenTheCopyIsReused() throws Exception {
        File source = source();
        File target = existing(INTEROP, SOURCE_TIME);
        byte[] partial = "partial".getBytes(StandardCharsets.UTF_8);
        Files.write(tmpOf(target).toPath(), partial);
        BppLaunch.readOnlyCopy(source, target);
        assertArrayEquals(partial, read(tmpOf(target)));
    }

    /** A Context whose every directory {@code prepare} can reach lies under the temporary folder. */
    ContextWrapper context() {
        return new ContextWrapper(null) {
            @Override public File getFilesDir() { return new File(tmp.getRoot(), "files"); }
            @Override public File getNoBackupFilesDir() { return new File(tmp.getRoot(), "no_backup"); }
            @Override public File getCacheDir() { return new File(tmp.getRoot(), "cache"); }
            @Override public File getExternalFilesDir(String type) { return new File(tmp.getRoot(), "external"); }
        };
    }

    List<String> tree() throws Exception {
        try (Stream<java.nio.file.Path> paths = Files.walk(tmp.getRoot().toPath())) {
            return paths.map(path -> tmp.getRoot().toPath().relativize(path).toString()).sorted()
                    .collect(Collectors.toList());
        }
    }

    @Test
    public void aRefusedDecisionThrowsBeforeAnyFileIsTouched() throws Exception {
        File devInterop = source();
        List<String> before = tree();
        int refused = 0;
        for (boolean development : new boolean[] {false, true}) {
            for (Tier tier : Tier.values()) {
                LaunchGate.Decision decision = LauncherScreenTest.decision(tier, development);
                if (decision.bppLaunch()) continue;
                refused++;
                LaunchCheck check = new LaunchCheck(null, null, null, null, null, decision, List.of(), null, false,
                        development ? devInterop : null);
                try {
                    BppLaunch.prepare(context(), check, new BppLaunch.Progress() {
                        @Override public void preparing() { fail("preparing in " + tier); }

                        @Override public void downloading(boolean interop, long received, long total) {
                            fail("downloading in " + tier);
                        }
                    });
                    fail(tier + " development=" + development + " prepared");
                } catch (java.io.IOException e) {
                    assertTrue("the gate refused, naming the tier: " + e.getMessage(),
                            e.getMessage().contains("launch tier " + tier));
                }
                assertEquals(tier + " development=" + development + " touched no file", before, tree());
            }
        }
        assertEquals("five tiers refuse without a Development interop, two with one", 7, refused);
    }

    @Test
    public void theGuardLetsEveryAllowedDecisionThrough() throws Exception {
        for (boolean development : new boolean[] {false, true}) {
            for (Tier tier : Tier.values()) {
                LaunchGate.Decision decision = LauncherScreenTest.decision(tier, development);
                if (decision.bppLaunch()) BppLaunch.refuseUnlessAllowed(decision);
            }
        }
    }
}
