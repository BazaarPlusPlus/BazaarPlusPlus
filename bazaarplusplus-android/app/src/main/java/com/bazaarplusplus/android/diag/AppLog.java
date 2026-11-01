package com.bazaarplusplus.android.diag;

import android.content.Context;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

/**
 * The App's own log lines: logcat, and {@code files/logs/app.log} in internal storage so a
 * diagnostics export after a failed launch still has them. The file keeps one previous
 * generation ({@code app.log.1}) once it passes {@link #LIMIT}.
 */
public final class AppLog {
    static final long LIMIT = 256 * 1024;
    private static File file;

    private AppLog() {}

    public static synchronized void init(Context context) {
        file = new File(context.getFilesDir(), "logs/app.log");
    }

    public static File file() {
        return file;
    }

    public static void i(String tag, String message) {
        Log.i(tag, message);
        append("I", tag, message, null);
    }

    public static void e(String tag, String message, Throwable error) {
        Log.e(tag, message, error);
        append("E", tag, message, error);
    }

    private static synchronized void append(String level, String tag, String message, Throwable error) {
        if (file == null) return;
        try {
            File parent = file.getParentFile();
            if (parent != null && !parent.isDirectory() && !parent.mkdirs()) return;
            if (file.length() > LIMIT) {
                File previous = new File(file.getPath() + ".1");
                previous.delete();
                if (!file.renameTo(previous)) return;
            }
            String line = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.ROOT).format(new Date()) + " " + level
                    + " " + tag + ": " + message + (error == null ? "" : "\n" + Log.getStackTraceString(error)) + "\n";
            try (OutputStream out = new FileOutputStream(file, true)) {
                out.write(line.getBytes(StandardCharsets.UTF_8));
            }
        } catch (IOException e) {
            // The log is best effort; logcat still has the line.
        }
    }
}
