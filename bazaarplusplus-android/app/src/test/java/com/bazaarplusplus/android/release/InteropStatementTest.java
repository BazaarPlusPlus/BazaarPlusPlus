package com.bazaarplusplus.android.release;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.TestZips;
import com.bazaarplusplus.android.game.GameCodeHash;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.MessageDigest;
import java.security.PublicKey;
import java.security.Signature;
import java.util.Base64;
import java.util.List;
import java.util.Map;

public class InteropStatementTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    static final GameCodeHash GAME = new GameCodeHash("a".repeat(64), "b".repeat(64));
    static final String ZIP = "c".repeat(64);
    /** The release certificate's SHA-256 (ADR 0009, 2026-11-01 key). */
    static final String RELEASE_CERT_SHA256 = "0d53bd4d2011c3e3181eb291c2154a0162f5dd9f4c4330b7bbc3728840851ce9";

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

    static String sign(KeyPair key, String zip, GameCodeHash game, String purpose) throws Exception {
        Signature signer = Signature.getInstance("SHA256withRSA");
        signer.initSign(key.getPrivate());
        signer.update(InteropStatement.text(zip, game, purpose));
        return Base64.getEncoder().encodeToString(signer.sign());
    }

    @Test
    public void theStatementIsTheAdrsFiveLines() {
        assertEquals("bpp-interop-v1\n" + ZIP + "\n" + "a".repeat(64) + "\n" + "b".repeat(64) + "\nrelease 6.1.0\n",
                new String(InteropStatement.text(ZIP, GAME, InteropStatement.releasePurpose("6.1.0")),
                        StandardCharsets.UTF_8));
    }

    @Test
    public void acceptsOnlyTheSignedZipGameAndPurpose() throws Exception {
        List<PublicKey> trusted = List.of(KEY.getPublic());
        String release = InteropStatement.releasePurpose("6.1.0");
        String signature = sign(KEY, ZIP, GAME, release);
        assertTrue(InteropStatement.verify(ZIP, GAME, release, signature, trusted));
        assertFalse("another release", InteropStatement.verify(ZIP, GAME, InteropStatement.releasePurpose("6.1.1"),
                signature, trusted));
        assertFalse("a release statement is not a pre-adaptation", InteropStatement.verify(ZIP, GAME,
                InteropStatement.PRE_ADAPTATION, signature, trusted));
        assertFalse("another zip", InteropStatement.verify("d".repeat(64), GAME, release, signature, trusted));
        assertFalse("another game", InteropStatement.verify(ZIP, new GameCodeHash("a".repeat(64), "e".repeat(64)),
                release, signature, trusted));
        assertFalse("an untrusted key", InteropStatement.verify(ZIP, GAME, release, sign(OTHER_KEY, ZIP, GAME, release),
                trusted));
        assertFalse("not base64", InteropStatement.verify(ZIP, GAME, release, "%%%", trusted));
        assertFalse("garbage", InteropStatement.verify(ZIP, GAME, release, "aW50ZXJvcA==", trusted));
    }

    @Test
    public void anyTrustedKeyMayHaveSigned() throws Exception {
        String signature = sign(OTHER_KEY, ZIP, GAME, InteropStatement.PRE_ADAPTATION);
        assertTrue(InteropStatement.verify(ZIP, GAME, InteropStatement.PRE_ADAPTATION, signature,
                List.of(KEY.getPublic(), OTHER_KEY.getPublic())));
    }

    @Test
    public void theEmbeddedReleaseCertificateIsPinned() throws Exception {
        // Unit tests run in app/; res/raw is not on the test classpath.
        File der = new File("src/main/res/raw/bpp_release_cert.der");
        byte[] bytes = Files.readAllBytes(der.toPath());
        StringBuilder hex = new StringBuilder();
        for (byte b : MessageDigest.getInstance("SHA-256").digest(bytes)) hex.append(String.format("%02x", b));
        assertEquals(RELEASE_CERT_SHA256, hex.toString());
        try (InputStream in = new FileInputStream(der)) {
            assertEquals("RSA", InteropStatement.certificateKey(in).getAlgorithm());
        }
    }

    @Test
    public void anInteropPackageHoldsOnlyInteropFiles() throws Exception {
        String root = InteropPackage.ENTRY_ROOT;
        assertNull(InteropPackage.strayEntry(TestZips.write(tmp.newFile("ok.zip"),
                Map.of(root + "Assembly-CSharp.dll", "a", root + "Il2Cppmscorlib.dll", "b"))));
        assertEquals("assets/LemonLoader/payload.json", InteropPackage.strayEntry(TestZips.write(tmp.newFile("shadow.zip"),
                Map.of(root + "Assembly-CSharp.dll", "a", "assets/LemonLoader/payload.json", "{}"))));
        assertEquals(root + "nested/x.dll", InteropPackage.strayEntry(TestZips.write(tmp.newFile("nested.zip"),
                Map.of(root + "nested/x.dll", "a"))));
        assertEquals(root + "../payload.json", InteropPackage.strayEntry(TestZips.write(tmp.newFile("slip.zip"),
                Map.of(root + "../payload.json", "a"))));
        assertEquals(root, InteropPackage.strayEntry(TestZips.write(tmp.newFile("dir.zip"), Map.of(root, ""))));
        assertEquals("(no entries)", InteropPackage.strayEntry(TestZips.write(tmp.newFile("empty.zip"), Map.of())));
    }
}
