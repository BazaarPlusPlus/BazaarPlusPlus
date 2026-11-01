package com.bazaarplusplus.android.loader;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;
import java.io.IOException;
import java.nio.file.Files;
import java.util.List;

public class LoaderDeploymentTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static final String INTEROP_A = "a".repeat(64);
    static final String INTEROP_B = "b".repeat(64);

    @Test
    public void aNewAppBuildResetsTheWholeLoaderDeployment() {
        // The spike's finding: a new App build with new runtime files was ignored, because
        // LemonLoader's revision is the game's lastUpdateTime, which an App update leaves alone.
        assertEquals(LoaderDeployment.RUNTIME_MARKERS, LoaderDeployment.stalePaths(
                new LoaderDeployment.Identity(6001000, INTEROP_A), new LoaderDeployment.Identity(6001001, INTEROP_A)));
        assertEquals(LoaderDeployment.RUNTIME_MARKERS,
                LoaderDeployment.stalePaths(null, new LoaderDeployment.Identity(6001000, INTEROP_A)));
    }

    @Test
    public void aNewInteropWithTheSameAppResetsOnlyTheInteropMarker() {
        assertEquals(List.of(".lemonloader-interop-hash"), LoaderDeployment.stalePaths(
                new LoaderDeployment.Identity(6001000, INTEROP_A), new LoaderDeployment.Identity(6001000, INTEROP_B)));
    }

    @Test
    public void anUnchangedLaunchKeepsEverything() {
        assertEquals(List.of(), LoaderDeployment.stalePaths(
                new LoaderDeployment.Identity(6001000, INTEROP_A), new LoaderDeployment.Identity(6001000, INTEROP_A)));
    }

    @Test
    public void applyDeletesTheStaleMarkersKeepsTheModsAndRecordsTheLaunch() throws IOException {
        File melon = tmp.newFolder("MelonLoader");
        File state = new File(tmp.getRoot(), "loader-deployment.json");
        File deploymentState = new File(melon, ".lemonloader-deployment-state/Mods");
        assertTrue(deploymentState.mkdirs());
        Files.write(new File(deploymentState, "BazaarPlusPlus.Core.dll").toPath(), new byte[] {1});
        Files.write(new File(melon, ".lemonloader-interop-hash").toPath(), "apk-1".getBytes());
        assertTrue(new File(melon, "Plugins").mkdirs());
        Files.write(new File(melon, "Plugins/BazaarPlusPlus.Updater.dll").toPath(), new byte[] {2});
        assertTrue(new File(melon, "Mods").mkdirs());
        Files.write(new File(melon, "Mods/BazaarPlusPlus.Core.dll").toPath(), new byte[] {3});

        List<String> deleted = LoaderDeployment.apply(state, melon, new LoaderDeployment.Identity(6001000, INTEROP_A));
        assertEquals(List.of(".lemonloader-deployment-state", ".lemonloader-interop-hash",
                "Plugins/BazaarPlusPlus.Updater.dll"), deleted);
        assertFalse(new File(melon, ".lemonloader-deployment-state").exists());
        assertTrue("the Updater owns Mods/", new File(melon, "Mods/BazaarPlusPlus.Core.dll").isFile());

        Files.write(new File(melon, ".lemonloader-interop-hash").toPath(), "apk-1".getBytes());
        assertEquals(List.of(), LoaderDeployment.apply(state, melon, new LoaderDeployment.Identity(6001000, INTEROP_A)));
        assertEquals(List.of(".lemonloader-interop-hash"),
                LoaderDeployment.apply(state, melon, new LoaderDeployment.Identity(6001000, INTEROP_B)));
    }

    @Test
    public void anUnreadableRecordCountsAsANewAppBuild() throws IOException {
        File state = tmp.newFile("loader-deployment.json");
        Files.write(state.toPath(), "{not json".getBytes());
        assertEquals(null, LoaderDeployment.read(state));
    }
}
