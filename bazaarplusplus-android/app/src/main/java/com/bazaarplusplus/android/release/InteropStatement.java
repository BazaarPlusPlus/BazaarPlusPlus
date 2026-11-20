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

// LemonLoader loads executable interop before the Updater runs.
// A release signature binds the zip to its game and purpose independently of the unsigned manifest.
public final class InteropStatement {
    private InteropStatement() {}

    public static String releasePurpose(String version) {
        return "release " + version;
    }

    public static byte[] text(String zipSha256, GameCodeHash game, String purpose) {
        return ("bpp-interop-v1\n" + zipSha256 + "\n" + game.il2cppSha256 + "\n" + game.metadataSha256 + "\n"
                + purpose + "\n").getBytes(StandardCharsets.UTF_8);
    }

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
            }
        }
        return false;
    }

    public static PublicKey certificateKey(InputStream der) throws IOException {
        try {
            X509Certificate cert = (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(der);
            return cert.getPublicKey();
        } catch (CertificateException e) {
            throw new IOException("Not an X.509 certificate", e);
        }
    }
}
