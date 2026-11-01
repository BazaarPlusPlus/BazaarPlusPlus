package com.bazaarplusplus.android.diag;

import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.net.Uri;
import android.os.Environment;
import android.provider.MediaStore;

import com.bazaarplusplus.android.io.AtomicFiles;

import java.io.File;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

/**
 * The diagnostics export: one zip in the shared Download folder (MediaStore, no permission or
 * FileProvider) with the launcher's state, the App's log, LemonLoader's Latest.log and the Launch
 * Record. Every text passes {@link Redactor} first.
 */
public final class Diagnostics {
    /** LemonLoader's log, relative to the external files directory; the first that exists is taken. */
    static final String[] LOADER_LOGS = {"MelonLoader/MelonLoader/Latest.log", "MelonLoader/Latest.log"};

    private Diagnostics() {}

    /** The files of one export, by entry name; missing sources are left out. */
    public static Map<String, String> collect(File external, File appLog, String launchRecordPath, String state) {
        Map<String, String> files = new LinkedHashMap<>();
        files.put("state.txt", state);
        String previous = AtomicFiles.readUtf8(new File(appLog.getPath() + ".1"));
        String current = AtomicFiles.readUtf8(appLog);
        if (previous != null || current != null) {
            files.put("app.log", (previous == null ? "" : previous) + (current == null ? "" : current));
        }
        for (String path : LOADER_LOGS) {
            String log = AtomicFiles.readUtf8(new File(external, path));
            if (log != null) {
                files.put("Latest.log", log);
                break;
            }
        }
        String record = AtomicFiles.readUtf8(new File(external, launchRecordPath));
        if (record != null) files.put("BazaarPlusPlus.launch.json", record);
        return files;
    }

    /** Writes each file, redacted, into one zip. */
    public static void zip(Map<String, String> files, OutputStream out) throws IOException {
        try (ZipOutputStream zip = new ZipOutputStream(out)) {
            for (Map.Entry<String, String> file : files.entrySet()) {
                zip.putNextEntry(new ZipEntry(file.getKey()));
                zip.write(Redactor.redact(file.getValue()).getBytes(StandardCharsets.UTF_8));
                zip.closeEntry();
            }
        }
    }

    /** Saves the export to Download/ and returns its file name. */
    public static String export(Context context, Map<String, String> files) throws IOException {
        String name = "BazaarPlusPlus-diagnostics-"
                + new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.ROOT).format(new Date()) + ".zip";
        ContentResolver resolver = context.getContentResolver();
        ContentValues values = new ContentValues();
        values.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
        values.put(MediaStore.MediaColumns.MIME_TYPE, "application/zip");
        values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS);
        values.put(MediaStore.MediaColumns.IS_PENDING, 1);
        Uri uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
        if (uri == null) throw new IOException("MediaStore refused " + name);
        try (OutputStream out = resolver.openOutputStream(uri)) {
            if (out == null) throw new IOException("Cannot open " + uri);
            zip(files, out);
        } catch (IOException | RuntimeException e) {
            resolver.delete(uri, null, null);
            throw e;
        }
        values.clear();
        values.put(MediaStore.MediaColumns.IS_PENDING, 0);
        resolver.update(uri, values, null, null);
        return name;
    }
}
