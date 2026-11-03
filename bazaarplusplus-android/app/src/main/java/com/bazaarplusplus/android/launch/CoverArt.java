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

/**
 * The launcher's cover (design/components/Launcher, Cover art): the Steam store's library hero
 * for The Bazaar, resolved at runtime from the public store API and cached under the App's cache
 * directory, as the iOS App does. Nothing is bundled: Valve rotates the hashed asset paths, and
 * the App must not redistribute store art. Offline, the last cached art answers; with none, the
 * cover stays plain.
 */
final class CoverArt {
    static final long STEAM_APP_ID = 1617400;
    private static final String API = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=";
    private static final String ASSET_HOST = "https://shared.akamai.steamstatic.com/store_item_assets/";
    private static final long MAX_AGE_MS = 3L * 24 * 3600 * 1000;
    private static final int META_LIMIT = 1 << 20;

    private CoverArt() {}

    /**
     * The cover decoded at least {@code minHeight} pixels tall where the art allows, or null. Runs
     * network and disk work; call it off the main thread.
     */
    static Bitmap load(File dir, int minHeight) {
        String url = heroUrl(dir);
        if (url == null) return null;
        File image = new File(dir, url.substring(url.lastIndexOf('/') + 1).replaceAll("[^A-Za-z0-9._-]", "_"));
        try {
            if (!image.isFile()) fetch(url, image);
        } catch (IOException e) {
            return null;
        }
        return decode(image, minHeight);
    }

    /** The cached hero URL, refreshed from the store when older than three days. */
    private static String heroUrl(File dir) {
        File meta = new File(dir, "hero.txt");
        String cached = meta.isFile() ? AtomicFiles.readUtf8(meta).trim() : null;
        if (cached != null && !cached.isEmpty() && System.currentTimeMillis() - meta.lastModified() < MAX_AGE_MS) {
            return cached;
        }
        try {
            String request = "{\"ids\":[{\"appid\":" + STEAM_APP_ID + "}],\"context\":{\"language\":\"schinese\","
                    + "\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}";
            String fresh = heroUrl(Download.text(Download.http(API + URLEncoder.encode(request, "UTF-8")), META_LIMIT));
            if (fresh == null) return cached;
            if (!dir.isDirectory() && !dir.mkdirs()) return fresh;
            AtomicFiles.writeUtf8(meta, fresh);
            return fresh;
        } catch (IOException | JSONException e) {
            return cached == null || cached.isEmpty() ? null : cached;
        }
    }

    /** The library hero's URL from an IStoreBrowseService/GetItems response, or null without one. */
    static String heroUrl(String response) throws JSONException {
        JSONArray items = new JSONObject(response).getJSONObject("response").optJSONArray("store_items");
        if (items == null || items.length() == 0) return null;
        JSONObject item = items.getJSONObject(0);
        if (item.optInt("success") != 1) return null;
        JSONObject assets = item.optJSONObject("assets");
        if (assets == null) return null;
        String format = assets.optString("asset_url_format", "steam/apps/" + STEAM_APP_ID + "/${FILENAME}");
        for (String key : new String[] {"library_hero", "library_hero_2x", "header_2x", "header"}) {
            String name = assets.optString(key);
            if (!name.isEmpty()) return ASSET_HOST + format.replace("${FILENAME}", name);
        }
        return null;
    }

    /** Downloads the art next to the others, then keeps only it: a rotated asset replaces the old one. */
    private static void fetch(String url, File image) throws IOException {
        File dir = image.getParentFile();
        File partial = new File(dir, image.getName() + ".part");
        try (InputStream in = Download.http(url).open(); OutputStream out = new FileOutputStream(partial)) {
            byte[] buffer = new byte[64 * 1024];
            for (int read; (read = in.read(buffer)) > 0; ) out.write(buffer, 0, read);
        }
        if (!partial.renameTo(image)) throw new IOException("cannot keep " + image);
        File[] others = dir.listFiles((d, name) -> !name.equals(image.getName()) && !name.equals("hero.txt"));
        if (others != null) for (File other : others) other.delete();
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
