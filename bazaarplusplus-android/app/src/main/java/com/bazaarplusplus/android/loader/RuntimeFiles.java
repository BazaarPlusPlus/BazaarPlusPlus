package com.bazaarplusplus.android.loader;

import android.content.Context;
import android.content.res.AssetFileDescriptor;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * The App's packaged files the game needs on disk: bridge.dex (the game class loader defines it)
 * and the Android App Runtime zip (a resource loader adds it to the game's assets). Each is
 * copied out of the App's assets once per App build and made read-only, because Android 14
 * refuses to load a writable dex.
 */
public final class RuntimeFiles {
    public static final String BRIDGE_ASSET = "bridge.dex";
    public static final String RUNTIME_ASSET = "lemon-runtime.zip";
    public static final String RUNTIME_FACTS_ASSET = "bpp-runtime.json";

    private RuntimeFiles() {}

    /** Whether this build carries a LemonLoader runtime; a development build may not. */
    public static boolean hasRuntime(Context context) {
        try (InputStream ignored = context.getAssets().open(RUNTIME_ASSET)) {
            return true;
        } catch (IOException e) {
            return false;
        }
    }

    /** The carried runtime's facts (Loader Release tag, nonManagedPayloadDigest). */
    public static RuntimeFacts facts(Context context) throws IOException {
        try (InputStream in = context.getAssets().open(RUNTIME_FACTS_ASSET)) {
            return RuntimeFacts.parse(new String(com.bazaarplusplus.android.io.AtomicFiles.readAtMost(in, 1 << 16),
                    java.nio.charset.StandardCharsets.UTF_8));
        } catch (org.json.JSONException e) {
            throw new IOException("Unreadable " + RUNTIME_FACTS_ASSET, e);
        }
    }

    public static File bridgeDex(Context context, long appVersionCode) throws IOException {
        return extract(context, BRIDGE_ASSET, new File(context.getCodeCacheDir(), appVersionCode + "-" + BRIDGE_ASSET));
    }

    public static File runtimeZip(Context context, long appVersionCode) throws IOException {
        return extract(context, RUNTIME_ASSET, new File(context.getNoBackupFilesDir(), appVersionCode + "-" + RUNTIME_ASSET));
    }

    private static File extract(Context context, String asset, File target) throws IOException {
        long expected;
        try (AssetFileDescriptor fd = context.getAssets().openFd(asset)) {
            expected = fd.getLength();
        }
        if (target.isFile() && target.length() == expected && !target.canWrite()) return target;
        File parent = target.getParentFile();
        File[] siblings = parent == null ? null : parent.listFiles();
        if (siblings != null) {
            for (File sibling : siblings) {
                if (sibling.getName().endsWith("-" + asset) && !sibling.equals(target)) sibling.delete();
            }
        }
        File tmp = new File(target.getPath() + ".tmp");
        tmp.delete();
        long started = System.nanoTime();
        try (InputStream in = context.getAssets().open(asset); OutputStream out = new FileOutputStream(tmp)) {
            byte[] buffer = new byte[1 << 20];
            int read;
            while ((read = in.read(buffer)) > 0) out.write(buffer, 0, read);
        }
        target.setWritable(true);
        target.delete();
        if (!tmp.renameTo(target)) throw new IOException("Cannot move " + tmp + " to " + target);
        if (!target.setWritable(false, false)) throw new IOException("Cannot make " + target + " read-only");
        Log.i("BppGame", "extracted " + asset + " (" + target.length() + " bytes) in "
                + (System.nanoTime() - started) / 1_000_000 + " ms");
        return target;
    }
}
