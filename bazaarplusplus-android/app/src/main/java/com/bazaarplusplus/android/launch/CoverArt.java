package com.bazaarplusplus.android.launch;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import com.bazaarplusplus.android.io.AtomicFiles;
import com.bazaarplusplus.android.release.Download;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URLEncoder;
import java.util.HashSet;
import java.util.Set;

/**
 * The launcher's cover art (design/components/Launcher, Cover art): two Steam store images for
 * The Bazaar, resolved at runtime from the public store API and cached under the App's cache
 * directory, as the iOS App does. The library hero (the hunter) is wide; the hero capsule (the
 * store's promotional art, with the logo) is portrait; {@link CoverView} picks one by the cover's
 * shape. Nothing is bundled: Valve rotates the hashed asset paths, and the App must not
 * redistribute store art. Offline, the last cached art answers; with none, the cover stays plain.
 */
final class CoverArt {
    static final long STEAM_APP_ID = 1617400;
    private static final String API = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=";
    private static final String ASSET_HOST = "https://shared.akamai.steamstatic.com/store_item_assets/";
    private static final String META = "art.txt";
    private static final long MAX_AGE_MS = 3L * 24 * 3600 * 1000;
    private static final int META_LIMIT = 1 << 20;

    /** The two images, either null when the store has none or it could not load. */
    static final class Images {
        final Bitmap hero;
        final Bitmap promo;

        Images(Bitmap hero, Bitmap promo) {
            this.hero = hero;
            this.promo = promo;
        }
    }

    /** The two images' URLs. */
    static final class Urls {
        final String hero;
        final String promo;

        Urls(String hero, String promo) {
            this.hero = hero;
            this.promo = promo;
        }
    }

    private CoverArt() {}

    /**
     * Both images, each decoded at least {@code minHeight} pixels tall where the art allows. Runs
     * network and disk work; call it off the main thread.
     */
    static Images load(File dir, int minHeight) {
        Urls urls = urls(dir);
        if (urls == null) return new Images(null, null);
        Set<String> keep = new HashSet<>();
        keep.add(META);
        Bitmap hero = image(dir, urls.hero, minHeight, keep);
        Bitmap promo = image(dir, urls.promo, minHeight, keep);
        // A rotated asset leaves its old file behind; keep only the current ones.
        File[] files = dir.listFiles((d, name) -> !keep.contains(name));
        if (files != null) for (File old : files) old.delete();
        return new Images(hero, promo);
    }

    private static Bitmap image(File dir, String url, int minHeight, Set<String> keep) {
        if (url == null) return null;
        File image = new File(dir, url.substring(url.lastIndexOf('/') + 1).replaceAll("[^A-Za-z0-9._-]", "_"));
        keep.add(image.getName());
        try {
            if (!image.isFile()) fetch(url, image);
        } catch (IOException e) {
            return null;
        }
        return decode(image, minHeight);
    }

    /** The cached URLs, refreshed from the store when older than three days. */
    private static Urls urls(File dir) {
        File meta = new File(dir, META);
        Urls cached = meta.isFile() ? parse(AtomicFiles.readUtf8(meta)) : null;
        if (cached != null && System.currentTimeMillis() - meta.lastModified() < MAX_AGE_MS) return cached;
        try {
            String request = "{\"ids\":[{\"appid\":" + STEAM_APP_ID + "}],\"context\":{\"language\":\"schinese\","
                    + "\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}";
            Urls fresh = urls(Download.text(Download.http(API + URLEncoder.encode(request, "UTF-8")), META_LIMIT));
            if (fresh == null) return cached;
            if (dir.isDirectory() || dir.mkdirs()) {
                AtomicFiles.writeUtf8(meta, (fresh.hero == null ? "" : fresh.hero) + "\n"
                        + (fresh.promo == null ? "" : fresh.promo) + "\n");
            }
            return fresh;
        } catch (IOException | JSONException e) {
            return cached;
        }
    }

    private static Urls parse(String meta) {
        String[] lines = meta.split("\n", -1);
        if (lines.length < 2) return null;
        String hero = lines[0].trim();
        String promo = lines[1].trim();
        return new Urls(hero.isEmpty() ? null : hero, promo.isEmpty() ? null : promo);
    }

    /** The images' URLs from an IStoreBrowseService/GetItems response, or null without any. */
    static Urls urls(String response) throws JSONException {
        JSONArray items = new JSONObject(response).getJSONObject("response").optJSONArray("store_items");
        if (items == null || items.length() == 0) return null;
        JSONObject item = items.getJSONObject(0);
        if (item.optInt("success") != 1) return null;
        JSONObject assets = item.optJSONObject("assets");
        if (assets == null) return null;
        String format = assets.optString("asset_url_format", "steam/apps/" + STEAM_APP_ID + "/${FILENAME}");
        String hero = asset(assets, format, "library_hero", "library_hero_2x", "header_2x", "header");
        String promo = asset(assets, format, "hero_capsule_2x", "hero_capsule", "library_capsule_2x", "library_capsule");
        return hero == null && promo == null ? null : new Urls(hero, promo);
    }

    private static String asset(JSONObject assets, String format, String... keys) {
        for (String key : keys) {
            String name = assets.optString(key);
            if (!name.isEmpty()) return ASSET_HOST + format.replace("${FILENAME}", name);
        }
        return null;
    }

    private static void fetch(String url, File image) throws IOException {
        File partial = new File(image.getParentFile(), image.getName() + ".part");
        try (InputStream in = Download.http(url).open(); OutputStream out = new FileOutputStream(partial)) {
            byte[] buffer = new byte[64 * 1024];
            for (int read; (read = in.read(buffer)) > 0; ) out.write(buffer, 0, read);
        }
        if (!partial.renameTo(image)) throw new IOException("cannot keep " + image);
    }

    /** Decodes no larger than needed: halves the art while it stays at least {@code minHeight} tall. */
    static Bitmap decode(File image, int minHeight) {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        BitmapFactory.decodeFile(image.getPath(), bounds);
        if (bounds.outHeight <= 0) {
            image.delete();
            return null;
        }
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = sampleSize(bounds.outHeight, minHeight);
        return BitmapFactory.decodeFile(image.getPath(), options);
    }

    static int sampleSize(int height, int minHeight) {
        int sample = 1;
        while (height / (sample * 2) >= minHeight) sample *= 2;
        return sample;
    }
}
