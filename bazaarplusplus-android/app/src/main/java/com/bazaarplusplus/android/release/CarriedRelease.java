package com.bazaarplusplus.android.release;

import android.content.res.AssetManager;

import com.bazaarplusplus.android.io.AtomicFiles;

import java.io.IOException;
import java.io.InputStream;

public final class CarriedRelease {
    private static final String DIR = "bpp-release";
    private static final int MAX_DOCUMENT = 1 << 20;

    private CarriedRelease() {}

    public static PlatformManifest manifest(AssetManager assets, ReleaseOrigin origin) {
        try (InputStream in = assets.open(DIR + "/release.json")) {
            return PlatformManifest.decodeCarried(new String(AtomicFiles.readAtMost(in, MAX_DOCUMENT),
                    java.nio.charset.StandardCharsets.UTF_8), origin);
        } catch (IOException | MalformedReleaseException e) {
            return null;
        }
    }

    public static Download.Source source(AssetManager assets, PlatformManifest.Artifact file) {
        String asset = DIR + "/" + file.sha256 + ".zip";
        try (InputStream ignored = assets.open(asset)) {
            return () -> assets.open(asset);
        } catch (IOException e) {
            return Download.http(file.url);
        }
    }
}
