package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

import android.content.ContextWrapper;

import com.bazaarplusplus.android.TestZips;
import com.bazaarplusplus.android.game.GameCodeHash;
import com.bazaarplusplus.android.launch.LaunchGate.Tier;
import com.bazaarplusplus.android.payload.ManagedPayloadStaging;
import com.bazaarplusplus.android.release.Download;
import com.bazaarplusplus.android.release.InteropPackage;
import com.bazaarplusplus.android.release.InteropStatement;
import com.bazaarplusplus.android.release.PlatformManifest;
import com.bazaarplusplus.android.release.ReleaseOrigin;

import org.json.JSONObject;
import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.ByteArrayInputStream;
import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.Signature;
import java.util.AbstractList;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.stream.Stream;

/**
 * {@link BppLaunch#prepare}'s steps over a recording {@link LaunchHost} and real directories under a
 * temporary folder, and {@link BppLaunch#readOnlyCopy}, the copy a Development interop loads from.
 */
public class BppLaunchTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static final byte[] INTEROP = "development interop".getBytes(StandardCharsets.UTF_8);
    /** Other bytes of the same length. */
    static final byte[] STALE = "DEVELOPMENT INTEROP".getBytes(StandardCharsets.UTF_8);
    static final long SOURCE_TIME = 1_700_000_000_000L;

    static final String RELEASE = "6.1.0";
    static final GameCodeHash GAME = LaunchGateTest.ADAPTED;
    static final String GAME_VERSION_NAME = "1.0.12825-production-android-d9601ded";
    static final long GAME_VERSION_CODE = 12825;
    static final GameCodeHash PLAY_GAME = LaunchGateTest.PLAY_ADAPTED;
    static final String PLAY_VERSION_NAME = "1.0.6";
    static final long PLAY_VERSION_CODE = 13168;
    /** What an earlier staging installed: files in Mods/ and UserLibs/, the version file among them. */
    static final List<String> INSTALLED_MODS = List.of("MelonLoader/Mods/BazaarPlusPlus.Core.dll",
            "MelonLoader/Mods/BazaarPlusPlus.version", "MelonLoader/UserLibs/Microsoft.Data.Sqlite.dll");
    /** Beside the Mods, and no Managed Payload Manifest names it. */
    static final String USER_DATA = "MelonLoader/UserData/BazaarPlusPlus.cfg";
    static final String SNAPSHOT = "5".repeat(64);
    static final LaunchRecord.App APP = new LaunchRecord.App("com.bazaarplusplus.android", RELEASE, 6001000,
            "6".repeat(64));
    static final long PID = 4242;
    static final KeyPair KEY = generate();
    static final KeyPair OTHER_KEY = generate();

    static KeyPair generate() {
        try {
            KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA");
            generator.initialize(2048);
            return generator.generateKeyPair();
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

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

    static byte[] read(File file) throws Exception {
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

    // ---- prepare ------------------------------------------------------------------------------

    /** A Context whose every directory lies under the temporary folder. */
    ContextWrapper context() {
        return new ContextWrapper(null) {
            @Override public File getFilesDir() { return new File(tmp.getRoot(), "files"); }
            @Override public File getNoBackupFilesDir() { return new File(tmp.getRoot(), "no_backup"); }
            @Override public File getCacheDir() { return new File(tmp.getRoot(), "cache"); }
            @Override public File getExternalFilesDir(String type) { return new File(tmp.getRoot(), "external"); }
        };
    }

    /** The App's layout under the temporary folder; none of it exists yet. */
    LaunchDirs dirs() {
        return AppFiles.launchDirs(context());
    }

    File record() {
        return new File(dirs().external, LaunchRecord.PATH);
    }

    /** Every path under the temporary folder: a directory, or a file's sha256 and whether it is writable. */
    Map<String, String> tree() throws Exception {
        Map<String, String> tree = new TreeMap<>();
        try (Stream<Path> paths = Files.walk(tmp.getRoot().toPath())) {
            for (Path path : (Iterable<Path>) paths::iterator) {
                File file = path.toFile();
                tree.put(tmp.getRoot().toPath().relativize(path).toString(),
                        file.isDirectory() ? "dir" : sha256(read(file)) + (file.canWrite() ? " rw" : " r"));
            }
        }
        return tree;
    }

    static String sha256(byte[] bytes) throws Exception {
        StringBuilder hex = new StringBuilder();
        for (byte b : MessageDigest.getInstance("SHA-256").digest(bytes)) hex.append(String.format("%02x", b));
        return hex.toString();
    }

    static void write(File file, String text) throws IOException {
        File parent = file.getParentFile();
        assertTrue(parent.isDirectory() || parent.mkdirs());
        Files.write(file.toPath(), text.getBytes(StandardCharsets.UTF_8));
    }

    static List<String> names(File dir) {
        String[] names = dir.list();
        List<String> sorted = names == null ? new ArrayList<>() : new ArrayList<>(List.of(names));
        sorted.sort(null);
        return sorted;
    }

    byte[] interopZip(boolean stray, String assembly) throws Exception {
        Map<String, String> entries = new LinkedHashMap<>();
        entries.put(InteropPackage.ENTRY_ROOT + "Assembly-CSharp.dll", assembly);
        if (stray) entries.put("assets/LemonLoader/payload.json", "{}");
        return read(TestZips.write(tmp.newFile(), entries));
    }

    /** A Managed Payload of {@link #RELEASE} whose Core.dll holds {@code core}. */
    byte[] payloadZip(String core) throws Exception {
        Map<String, String> entries = new LinkedHashMap<>();
        entries.put("manifest.txt", "bpp-managed-payload-manifest 1\nversion " + RELEASE + "\nplatform android-aarch64\n");
        entries.put("manifest.txt.minisig", "untrusted comment: x\nRW...\n");
        entries.put("files/MelonLoader/Mods/BazaarPlusPlus.Core.dll", core);
        return read(TestZips.write(tmp.newFile(), entries));
    }

    static String sign(KeyPair key, String zipSha256, GameCodeHash game, String purpose) throws Exception {
        Signature signer = Signature.getInstance("SHA256withRSA");
        signer.initSign(key.getPrivate());
        signer.update(InteropStatement.text(zipSha256, game, purpose));
        return Base64.getEncoder().encodeToString(signer.sign());
    }

    static JSONObject artifact(String url, byte[] bytes) throws Exception {
        return new JSONObject().put("url", url).put("size", bytes.length).put("sha256", sha256(bytes));
    }

    /** One adaptation of {@link #RELEASE}: its game, and these zips. */
    static JSONObject adaptationJson(String source, String signer, String gameVersion, long versionCode, GameCodeHash game,
            byte[] interop, byte[] payload, String signature) throws Exception {
        return new JSONObject()
                .put("signerSha256", signer)
                .put("game", new JSONObject().put("gameVersion", gameVersion).put("versionCode", versionCode)
                        .put("il2cppSha256", game.il2cppSha256).put("metadataSha256", game.metadataSha256))
                .put("interop", artifact(ReleaseOrigin.PUBLIC.interopUrl(sha256(interop)), interop)
                        .put("snapshotSha256", SNAPSHOT).put("signature", signature))
                .put("managedPayload", artifact(ReleaseOrigin.PUBLIC.managedPayloadUrl(RELEASE, source), payload)
                        .put("signature", "managed-signature-" + source));
    }

    /**
     * A Platform Release Manifest of {@link #RELEASE} with a publisher and a Play adaptation,
     * decoded as the App decodes one. It leaves out the top-level fields that only older Apps read.
     */
    static PlatformManifest manifest(JSONObject publisher, JSONObject play) throws Exception {
        String versioned = ReleaseOrigin.PUBLIC.base + "/" + RELEASE + "/android-aarch64";
        JSONObject record = new JSONObject()
                .put("app", artifact(versioned + "/app/BazaarPlusPlus.apk", "apk".getBytes(StandardCharsets.UTF_8))
                        .put("versionCode", APP.versionCode).put("signerSha256", "0".repeat(64)))
                .put("adaptations", new JSONObject().put("publisher", publisher).put("play", play))
                .put("loader", new JSONObject().put("tag", "v0.7.4-android.4").put("sha256", "8".repeat(64)))
                .put("selfUpdatable", true)
                .put("historyDatabaseUserVersion", 1)
                .put("nonManagedPayloadDigest", "7".repeat(64));
        JSONObject root = new JSONObject().put("version", RELEASE).put("gitCommit", "c".repeat(40))
                .put("downloads", new JSONObject().put("android-aarch64", record));
        return PlatformManifest.decode(root.toString(), ReleaseOrigin.PUBLIC);
    }

    /**
     * One release a test serves through {@link FakeHost}. {@code signer} signs the publisher's
     * statement for {@code signedFor}; the Play statement is always good.
     */
    final class Release {
        final byte[] interop;
        final byte[] payload;
        final byte[] playInterop;
        final byte[] playPayload;
        final PlatformManifest manifest;

        Release(boolean stray, KeyPair signer, GameCodeHash signedFor) throws Exception {
            String purpose = InteropStatement.releasePurpose(RELEASE);
            interop = interopZip(stray, "assembly");
            payload = payloadZip("core");
            playInterop = interopZip(false, "play assembly");
            playPayload = payloadZip("play core");
            manifest = manifest(
                    adaptationJson("publisher", LaunchGateTest.PUBLISHER_SIGNER, GAME_VERSION_NAME, GAME_VERSION_CODE, GAME,
                            interop, payload, sign(signer, sha256(interop), signedFor, purpose)),
                    adaptationJson("play", LaunchGateTest.PLAY_SIGNER, PLAY_VERSION_NAME, PLAY_VERSION_CODE, PLAY_GAME,
                            playInterop, playPayload, sign(KEY, sha256(playInterop), PLAY_GAME, purpose)));
        }

        Release() throws Exception {
            this(false, KEY, GAME);
        }

        PlatformManifest.Adaptation adaptation(String source) {
            return manifest.adaptations.get(source);
        }

        File cachedZip() throws Exception {
            return new File(dirs().interopCache, sha256(interop) + ".zip");
        }
    }

    /** {@link LaunchHost} that serves {@link Release}'s zips and writes down every call in order. */
    static final class FakeHost implements LaunchHost {
        final List<String> events = new ArrayList<>();
        final Release release;
        boolean prepared;
        Exception prepareFails;
        File preparedWith;
        /** Runs inside {@link #prepareGame}, to look at the files at that moment. */
        Runnable probe = () -> {};
        /** The artifacts served with bytes other than the manifest's. */
        Set<String> corrupt = Set.of();

        FakeHost(Release release) {
            this.release = release;
        }

        /** "interop" and "managed" for the publisher's zips, "play interop" and "play managed" for Play's. */
        String name(PlatformManifest.Artifact file) {
            if (release != null) {
                for (PlatformManifest.Adaptation adaptation : release.manifest.adaptations.values()) {
                    String prefix = adaptation.source.equals(PlatformManifest.PUBLISHER) ? "" : adaptation.source + " ";
                    if (file == adaptation.interop.file) return prefix + "interop";
                    if (file == adaptation.managedPayload.file) return prefix + "managed";
                }
            }
            throw new AssertionError("an artifact of no release: " + file.url);
        }

        byte[] bytes(String name) {
            if (corrupt.contains(name)) return "corrupt".getBytes(StandardCharsets.UTF_8);
            switch (name) {
                case "interop": return release.interop;
                case "managed": return release.payload;
                case "play interop": return release.playInterop;
                case "play managed": return release.playPayload;
                default: throw new AssertionError(name);
            }
        }

        @Override public boolean gamePrepared() {
            events.add("gamePrepared");
            return prepared;
        }

        @Override public Download.Source source(PlatformManifest.Artifact file) {
            String name = name(file);
            events.add("source " + name);
            return () -> {
                events.add("open " + name);
                return new ByteArrayInputStream(bytes(name));
            };
        }

        @Override public long appVersionCode() {
            events.add("appVersionCode");
            return APP.versionCode;
        }

        @Override public LaunchRecord.App app() {
            events.add("app");
            return APP;
        }

        @Override public void prepareGame(File interopZip) throws Exception {
            events.add("prepareGame");
            preparedWith = interopZip;
            probe.run();
            if (prepareFails != null) throw prepareFails;
        }

        @Override public long processId() {
            events.add("processId");
            return PID;
        }

        List<String> sources() {
            List<String> sources = new ArrayList<>();
            for (String event : events) if (event.startsWith("source ") || event.startsWith("open ")) sources.add(event);
            return sources;
        }
    }

    /** The trusted keys, writing "verify" to {@code events} each time the statement check reads them. */
    static List<PublicKey> trusted(List<String> events, PublicKey... keys) {
        return new AbstractList<PublicKey>() {
            @Override public PublicKey get(int index) { return keys[index]; }

            @Override public int size() { return keys.length; }

            @Override public Iterator<PublicKey> iterator() {
                events.add("verify");
                return super.iterator();
            }
        };
    }

    /** A NORMAL launch of the publisher's game. */
    LaunchInputs inputs(FakeHost host, String installedMods) {
        return new LaunchInputs(LauncherScreenTest.decision(Tier.NORMAL, false), true, GAME, GAME_VERSION_NAME,
                GAME_VERSION_CODE, host.release.manifest, host.release.adaptation(PlatformManifest.PUBLISHER),
                installedMods, trusted(host.events, KEY.getPublic()), null);
    }

    /** A NORMAL launch of Google Play's game. */
    LaunchInputs playInputs(FakeHost host, String installedMods) {
        LaunchGate.Decision decision = LaunchGate.decide(new LaunchGate.Inputs(LaunchGateTest.installed(
                PLAY_VERSION_CODE, PLAY_GAME, Set.of(LaunchGateTest.PLAY_SIGNER)), LaunchGateTest.RELEASE, false));
        assertEquals(Tier.NORMAL, decision.tier);
        return new LaunchInputs(decision, true, PLAY_GAME, PLAY_VERSION_NAME, PLAY_VERSION_CODE, host.release.manifest,
                host.release.adaptation(PlatformManifest.PLAY), installedMods, trusted(host.events, KEY.getPublic()), null);
    }

    static final BppLaunch.Progress SILENT = new BppLaunch.Progress() {
        @Override public void preparing() {}

        @Override public void downloading(boolean interop, long received, long total) {}
    };

    void prepare(FakeHost host, LaunchInputs in) throws Exception {
        new BppLaunch(host, dirs()).prepare(in, SILENT);
    }

    /** What an earlier launch of the publisher's game left in every directory of {@link #dirs}. */
    void earlierLaunch() throws Exception {
        LaunchDirs dirs = dirs();
        write(new File(dirs.interopCache, "0".repeat(64) + ".zip"), "an older Interop Package");
        write(new File(dirs.interopCache, "development.zip"), "a Development interop");
        write(new File(dirs.managedPayloadCache, "1".repeat(64) + ".zip"), "an older Managed Payload");
        write(new File(dirs.updates, "6.0.9/manifest.txt"), "version 6.0.9\n");
        write(dirs.stagedVersion, "6.0.9\n");
        write(dirs.stagedGame, GAME + "\n");
        write(dirs.loaderDeployment, "{\"appVersionCode\":6000900,\"interopSha256\":\"" + "0".repeat(64) + "\"}");
        write(record(), "{\"processId\": 1}\n");
    }

    /** Installed Mods of {@code version}: {@link #INSTALLED_MODS}, the manifest that names them, and {@link #USER_DATA}. */
    void installMods(String version) throws Exception {
        File external = dirs().external;
        StringBuilder manifest = new StringBuilder("bpp-managed-payload-manifest 1\nversion " + version
                + "\nplatform android-aarch64\nhistory-database-user-version 1\nnon-managed-payload-digest "
                + "7".repeat(64) + "\n");
        for (String path : INSTALLED_MODS) {
            write(new File(external, path), "installed " + path);
            manifest.append("file ").append("0".repeat(64)).append(" 4 ").append(path).append('\n');
        }
        write(new File(external, ManagedPayloadStaging.INSTALLED_MANIFEST), manifest.toString());
        write(new File(external, USER_DATA), "settings");
    }

    /** Whether each of {@link #INSTALLED_MODS} and their manifest is still there, and {@link #USER_DATA}. */
    List<Boolean> installedMods() {
        File external = dirs().external;
        List<Boolean> present = new ArrayList<>();
        for (String path : INSTALLED_MODS) present.add(new File(external, path).exists());
        present.add(new File(external, ManagedPayloadStaging.INSTALLED_MANIFEST).exists());
        present.add(new File(external, USER_DATA).exists());
        return present;
    }

    static final List<Boolean> MODS_KEPT = List.of(true, true, true, true, true);
    static final List<Boolean> MODS_REMOVED = List.of(false, false, false, false, true);

    static String text(File file) throws Exception {
        return new String(read(file), StandardCharsets.UTF_8);
    }

    @Test
    public void aStatementThatDoesNotVerifyOpensNoSourceAndChangesNoFile() throws Exception {
        for (Release release : List.of(new Release(false, OTHER_KEY, GAME),
                new Release(false, KEY, LaunchGateTest.OTHER))) {
            earlierLaunch();
            Map<String, String> before = tree();
            FakeHost host = new FakeHost(release);
            IOException e = assertThrows(IOException.class, () -> prepare(host, inputs(host, "6.0.0")));
            assertTrue(e.getMessage(), e.getMessage().contains("does not verify"));
            assertEquals("no source requested or opened", List.of(), host.sources());
            assertNull("the game is not prepared", host.preparedWith);
            assertEquals("no file changed", before, tree());
        }
    }

    @Test
    public void aStrayEntryDeletesTheZipAndStopsBeforeStaging() throws Exception {
        Release release = new Release(true, KEY, GAME);
        LaunchDirs dirs = dirs();
        write(dirs.loaderDeployment, "{\"appVersionCode\":6000900,\"interopSha256\":\"" + "0".repeat(64) + "\"}");
        byte[] loaderState = read(dirs.loaderDeployment);
        FakeHost host = new FakeHost(release);
        IOException e = assertThrows(IOException.class, () -> prepare(host, inputs(host, "6.0.0")));
        assertEquals("The Interop Package holds assets/LemonLoader/payload.json", e.getMessage());
        assertEquals("the zip was downloaded", List.of("source interop", "open interop"), host.sources());
        assertFalse("the zip is deleted", release.cachedZip().exists());
        assertEquals("nothing staged", List.of(), names(dirs.updates));
        assertFalse(dirs.stagedVersion.exists());
        assertFalse(dirs.stagedGame.exists());
        assertArrayEquals("the loader deployment state is unchanged", loaderState, read(dirs.loaderDeployment));
        assertFalse("no Launch Record", record().exists());
        assertNull("the game is not prepared", host.preparedWith);
    }

    @Test
    public void installedModsAtTheReleaseDownloadNoPayloadAndEmptyUpdates() throws Exception {
        earlierLaunch();
        installMods(RELEASE);
        FakeHost host = new FakeHost(new Release());
        prepare(host, inputs(host, RELEASE));
        assertEquals("only the Interop Package", List.of("source interop", "open interop"), host.sources());
        assertTrue(dirs().updates.isDirectory());
        assertEquals("Updates/ is empty", List.of(), names(dirs().updates));
        assertFalse("the staged-version state file is deleted", dirs().stagedVersion.exists());
        assertEquals("the same game keeps its record", GAME + "\n", text(dirs().stagedGame));
        assertEquals("the same game keeps its Mods", MODS_KEPT, installedMods());
        assertTrue(record().isFile());
    }

    @Test
    public void aGameSwitchReplacesTheInstalledModsEvenOfTheSameVersion() throws Exception {
        earlierLaunch();
        installMods(RELEASE);
        FakeHost host = new FakeHost(new Release());
        prepare(host, playInputs(host, RELEASE));
        assertEquals("Play's zips only", List.of("source play interop", "open play interop", "source play managed",
                "open play managed"), host.sources());
        assertEquals("the publisher's Mods and their manifest are gone", MODS_REMOVED, installedMods());
        assertEquals(List.of(RELEASE), names(dirs().updates));
        assertEquals("Play's Managed Payload is staged", "play core",
                text(new File(dirs().updates, RELEASE + "/files/MelonLoader/Mods/BazaarPlusPlus.Core.dll")));
        assertEquals(RELEASE + "\n", text(dirs().stagedVersion));
        assertEquals(PLAY_GAME + "\n", text(dirs().stagedGame));
        assertEquals(host.preparedWith, new File(dirs().interopCache, sha256(host.release.playInterop) + ".zip"));
        JSONObject written = new JSONObject(text(record()));
        assertEquals(sha256(host.release.playInterop), written.getJSONObject("interop").getString("sha256"));
        assertEquals(PLAY_GAME.il2cppSha256, written.getJSONObject("game").getString("il2cppSha256"));
    }

    @Test
    public void aSwitchWhosePayloadDoesNotDownloadKeepsTheInstalledModsAndTheRecord() throws Exception {
        earlierLaunch();
        installMods(RELEASE);
        FakeHost host = new FakeHost(new Release());
        host.corrupt = Set.of("play managed");
        assertThrows(IOException.class, () -> prepare(host, playInputs(host, RELEASE)));
        assertEquals(MODS_KEPT, installedMods());
        assertEquals("the switch is still to do", GAME + "\n", text(dirs().stagedGame));
        assertNull(host.preparedWith);
    }

    @Test
    public void anAppThatRecordedNoGameReplacesTheInstalledModsOnce() throws Exception {
        // An App before ADR 0019 staged these Mods and recorded no game.
        installMods(RELEASE);
        FakeHost first = new FakeHost(new Release());
        prepare(first, inputs(first, RELEASE));
        assertEquals(List.of("source interop", "open interop", "source managed", "open managed"), first.sources());
        assertEquals(MODS_REMOVED, installedMods());
        assertEquals(GAME + "\n", text(dirs().stagedGame));

        // The Updater swapped them in; the next launch stages nothing.
        installMods(RELEASE);
        FakeHost next = new FakeHost(first.release);
        prepare(next, inputs(next, RELEASE));
        assertEquals("the cached Interop Package, and no Managed Payload", List.of("source interop"), next.sources());
        assertEquals(MODS_KEPT, installedMods());
        assertEquals(List.of(), names(dirs().updates));
    }

    @Test
    public void olderInstalledModsLeaveOnlyTheReleaseInUpdates() throws Exception {
        earlierLaunch();
        write(new File(dirs().updates, ".download-6.0.9-0123"), "a staging that stopped");
        FakeHost host = new FakeHost(new Release());
        prepare(host, inputs(host, "6.0.0"));
        assertEquals(List.of("source interop", "open interop", "source managed", "open managed"), host.sources());
        assertEquals(List.of(RELEASE), names(dirs().updates));
        assertEquals("core", text(new File(dirs().updates, RELEASE + "/files/MelonLoader/Mods/BazaarPlusPlus.Core.dll")));
        assertEquals(RELEASE + "\n", new String(read(dirs().stagedVersion), StandardCharsets.UTF_8));
        assertEquals(GAME + "\n", text(dirs().stagedGame));
        assertEquals("the managed cache keeps only this zip",
                List.of(sha256(host.release.payload) + ".zip"), names(dirs().managedPayloadCache));
    }

    @Test
    public void noLaunchRecordWhenPreparingTheGameFails() throws Exception {
        FakeHost host = new FakeHost(new Release());
        host.prepareFails = new IllegalStateException("no game");
        Exception e = assertThrows(Exception.class, () -> prepare(host, inputs(host, "6.0.0")));
        assertEquals("no game", e.getMessage());
        assertFalse(record().exists());
        assertEquals(host.release.cachedZip(), host.preparedWith);
    }

    @Test
    public void theLaunchRecordNamesTheInputsFieldByField() throws Exception {
        FakeHost host = new FakeHost(new Release());
        prepare(host, inputs(host, "6.0.0"));
        String written = new String(read(record()), StandardCharsets.UTF_8);
        assertEquals("{\n"
                + "  \"schemaVersion\": 1,\n"
                + "  \"processId\": " + PID + ",\n"
                + "  \"app\": {\n"
                + "    \"packageName\": \"" + APP.packageName + "\",\n"
                + "    \"versionName\": \"" + APP.versionName + "\",\n"
                + "    \"versionCode\": " + APP.versionCode + ",\n"
                + "    \"nonManagedPayloadDigest\": \"" + APP.nonManagedPayloadDigest + "\"\n"
                + "  },\n"
                + "  \"release\": \"" + RELEASE + "\",\n"
                + "  \"game\": {\n"
                + "    \"packageName\": \"com.tempogames.thebazaar\",\n"
                + "    \"versionName\": \"" + GAME_VERSION_NAME + "\",\n"
                + "    \"versionCode\": " + GAME_VERSION_CODE + ",\n"
                + "    \"il2cppSha256\": \"" + GAME.il2cppSha256 + "\",\n"
                + "    \"metadataSha256\": \"" + GAME.metadataSha256 + "\"\n"
                + "  },\n"
                + "  \"interop\": {\n"
                + "    \"sha256\": \"" + sha256(host.release.interop) + "\",\n"
                + "    \"snapshotSha256\": \"" + SNAPSHOT + "\"\n"
                + "  }\n"
                + "}\n", written);
        assertEquals("the golden's shape", written, LaunchRecordTest.fromJson(new JSONObject(written)).toJson());
    }

    @Test
    public void anAlreadyPreparedGameOrNoRuntimeThrowsBeforeAnyFileEffect() throws Exception {
        Release release = new Release();
        FakeHost prepared = new FakeHost(release);
        prepared.prepared = true;
        FakeHost noRuntime = new FakeHost(release);
        LaunchInputs normal = inputs(noRuntime, "6.0.0");
        LaunchInputs withoutRuntime = new LaunchInputs(normal.decision, false, normal.hash, normal.gameVersionName,
                normal.gameVersionCode, normal.manifest, normal.adaptation, normal.installedModsVersion, normal.trusted,
                normal.devInterop);
        Map<String, String> before = tree();
        IOException e = assertThrows(IOException.class, () -> prepare(prepared, inputs(prepared, "6.0.0")));
        assertTrue(e.getMessage(), e.getMessage().startsWith("The game already ran in this process"));
        assertEquals(List.of("gamePrepared"), prepared.events);
        e = assertThrows(IOException.class, () -> prepare(noRuntime, withoutRuntime));
        assertEquals("This build carries no Android App Runtime", e.getMessage());
        assertEquals(List.of(), noRuntime.events);
        assertEquals("not even the game storage is created", before, tree());
        assertFalse(dirs().external.exists());
    }

    @Test
    public void aDevelopmentInteropNeedsNoManifestAndDeletesAStaleRecordAfterThePreparedGame() throws Exception {
        earlierLaunch();
        File devInterop = source();
        FakeHost host = new FakeHost(null);
        boolean[] recordAtPrepare = {false};
        host.probe = () -> recordAtPrepare[0] = record().exists();
        LaunchInputs in = new LaunchInputs(LauncherScreenTest.decision(Tier.OFFLINE_NO_CACHE, true), true, GAME,
                GAME_VERSION_NAME, GAME_VERSION_CODE, null, null, "6.0.0", trusted(host.events, KEY.getPublic()),
                devInterop);
        prepare(host, in);
        File copy = new File(dirs().interopCache, "development.zip");
        assertEquals("the game gets the read-only copy", copy, host.preparedWith);
        assertArrayEquals(INTEROP, read(copy));
        assertFalse(copy.canWrite());
        assertTrue("the stale record is deleted only after the game is prepared", recordAtPrepare[0]);
        assertFalse("no Launch Record", record().exists());
        assertEquals("no statement, no source, no record", List.of("gamePrepared", "appVersionCode", "prepareGame"),
                host.events);
        assertEquals("staging is not touched", "6.0.9\n", new String(read(dirs().stagedVersion), StandardCharsets.UTF_8));
        assertEquals("nor the staged game", GAME + "\n", text(dirs().stagedGame));
    }

    @Test
    public void aNormalLaunchRemovesALeftoverDevelopmentZip() throws Exception {
        earlierLaunch();
        FakeHost host = new FakeHost(new Release());
        prepare(host, inputs(host, RELEASE));
        assertEquals(List.of(host.release.cachedZip().getName()), names(dirs().interopCache));
    }

    @Test
    public void theStepsRunInOrder() throws Exception {
        earlierLaunch();
        FakeHost host = new FakeHost(new Release());
        LaunchDirs dirs = dirs();
        List<String> atPrepare = new ArrayList<>();
        host.probe = () -> {
            atPrepare.add("updates " + names(dirs.updates));
            atPrepare.add("staged " + dirs.stagedVersion.exists());
            atPrepare.add("loader " + new String(readQuietly(dirs.loaderDeployment), StandardCharsets.UTF_8));
            atPrepare.add("record " + new String(readQuietly(record()), StandardCharsets.UTF_8));
        };
        prepare(host, inputs(host, "6.0.0"));
        assertEquals(List.of("gamePrepared", "appVersionCode", "verify", "source interop", "open interop",
                "source managed", "open managed", "prepareGame", "processId", "app"), host.events);
        String interopSha256 = sha256(host.release.interop);
        assertEquals("staged, pruned and the loader deployment reset before the game is prepared; the record after",
                List.of("updates [" + RELEASE + "]", "staged true",
                        "loader " + new JSONObject().put("appVersionCode", APP.versionCode)
                                .put("interopSha256", interopSha256),
                        "record {\"processId\": 1}\n"), atPrepare);
        assertTrue(new String(read(record()), StandardCharsets.UTF_8).contains("\"processId\": " + PID));
    }

    static byte[] readQuietly(File file) {
        try {
            return read(file);
        } catch (Exception e) {
            return new byte[0];
        }
    }

    // ---- the gate guard -----------------------------------------------------------------------

    @Test
    public void aRefusedDecisionThrowsBeforeAnyFileIsTouched() throws Exception {
        File devInterop = source();
        Map<String, String> before = tree();
        int refused = 0;
        for (boolean development : new boolean[] {false, true}) {
            for (Tier tier : Tier.values()) {
                LaunchGate.Decision decision = LauncherScreenTest.decision(tier, development);
                if (decision.bppLaunch()) continue;
                refused++;
                LaunchCheck check = new LaunchCheck(null, null, null, null, null, decision, List.of(), null, false,
                        development ? devInterop : null);
                FakeHost host = new FakeHost(null);
                try {
                    new BppLaunch(host, dirs()).prepare(check.inputs(), new BppLaunch.Progress() {
                        @Override public void preparing() { fail("preparing in " + tier); }

                        @Override public void downloading(boolean interop, long received, long total) {
                            fail("downloading in " + tier);
                        }
                    });
                    fail(tier + " development=" + development + " prepared");
                } catch (IOException e) {
                    assertTrue("the gate refused, naming the tier: " + e.getMessage(),
                            e.getMessage().contains("launch tier " + tier));
                }
                assertEquals(tier + " development=" + development + " asked the host nothing", List.of(), host.events);
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

    @Test
    public void theAppsLaunchDirsAreAppFilesPaths() throws Exception {
        LaunchDirs dirs = dirs();
        ContextWrapper context = context();
        assertEquals(AppFiles.interopCache(context), dirs.interopCache);
        assertEquals(AppFiles.managedPayloadCache(context), dirs.managedPayloadCache);
        assertEquals(AppFiles.stagedVersion(context), dirs.stagedVersion);
        assertEquals(AppFiles.stagedGame(context), dirs.stagedGame);
        assertEquals(AppFiles.loaderDeployment(context), dirs.loaderDeployment);
        assertFalse("launchDirs creates nothing", dirs.external.exists());
        assertEquals(AppFiles.external(context), dirs.external);
        assertEquals(AppFiles.updates(context), dirs.updates);
    }
}
