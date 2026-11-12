package com.bazaarplusplus.android.release;

import android.content.res.AssetManager;

import com.bazaarplusplus.android.io.AtomicFiles;

import java.io.IOException;
import java.io.InputStream;

/**
 * The release a release APK carries (ADR 0012, ADR 0019): every adaptation's Interop Package and
 * Managed Payload zip under {@code assets/bpp-release/<sha256>.zip}, named by
 * {@code assets/bpp-release/release.json}. A new install needs no download to run the release it
 * came with. A build without them reads the network.
 */
public final class CarriedRelease {
    private static final String DIR = "bpp-release";
    private static final int MAX_DOCUMENT = 1 << 20;

    private CarriedRelease() {}

    /** The carried release, or null when this build carries none or {@code origin} is not its origin. */
    public static PlatformManifest manifest(AssetManager assets, ReleaseOrigin origin) {
        try (InputStream in = assets.open(DIR + "/release.json")) {
            return PlatformManifest.decodeCarried(new String(AtomicFiles.readAtMost(in, MAX_DOCUMENT),
                    java.nio.charset.StandardCharsets.UTF_8), origin);
        } catch (IOException | MalformedReleaseException e) {
            return null;
        }
    }

    /** The APK's copy of {@code file} when it carries one, the network otherwise; the caller checks the bytes. */
    public static Download.Source source(AssetManager assets, PlatformManifest.Artifact file) {
        String asset = DIR + "/" + file.sha256 + ".zip";
        try (InputStream ignored = assets.open(asset)) {
            return () -> assets.open(asset);
        } catch (IOException e) {
            return Download.http(file.url);
        }
    }
}
