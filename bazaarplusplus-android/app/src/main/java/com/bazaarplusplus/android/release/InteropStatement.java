package com.bazaarplusplus.android.release;

import com.bazaarplusplus.android.game.GameCodeHash;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.PublicKey;
import java.security.Signature;
import java.security.cert.CertificateException;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.util.Base64;
import java.util.List;

/**
 * The signed interop statement of ADR 0012 ("interop 的签名"): it binds an Interop Package's zip
 * sha256 to the Game Code Hash it was generated for and to its purpose, {@code release <version>}
 * or {@code pre-adaptation}. Signed with SHA256withRSA by the Android release key (ADR 0009);
 * the App verifies it against the release certificate it carries.
 */
public final class InteropStatement {
    public static final String PRE_ADAPTATION = "pre-adaptation";

    private InteropStatement() {}

    /** The purpose a normal launch of {@code version}'s release accepts. */
    public static String releasePurpose(String version) {
        return "release " + version;
    }

    public static byte[] text(String zipSha256, GameCodeHash game, String purpose) {
        return ("bpp-interop-v1\n" + zipSha256 + "\n" + game.il2cppSha256 + "\n" + game.metadataSha256 + "\n"
                + purpose + "\n").getBytes(StandardCharsets.UTF_8);
    }

    /** Whether any trusted key signed the statement for this zip, game code and purpose. */
    public static boolean verify(String zipSha256, GameCodeHash game, String purpose, String signatureBase64,
            List<PublicKey> trusted) {
        byte[] signature;
        try {
            signature = Base64.getDecoder().decode(signatureBase64.trim());
        } catch (IllegalArgumentException e) {
            return false;
        }
        byte[] statement = text(zipSha256, game, purpose);
        for (PublicKey key : trusted) {
            try {
                Signature verifier = Signature.getInstance("SHA256withRSA");
                verifier.initVerify(key);
                verifier.update(statement);
                if (verifier.verify(signature)) return true;
            } catch (GeneralSecurityException e) {
                // A malformed signature or a key of another kind: this key did not sign it.
            }
        }
        return false;
    }

    /** The public key of a DER (or PEM) X.509 certificate. */
    public static PublicKey certificateKey(InputStream der) throws IOException {
        try {
            X509Certificate cert = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(der);
            return cert.getPublicKey();
        } catch (CertificateException e) {
            throw new IOException("Not an X.509 certificate", e);
        }
    }
}
