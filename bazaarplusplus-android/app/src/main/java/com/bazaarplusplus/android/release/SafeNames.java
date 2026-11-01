package com.bazaarplusplus.android.release;

/** Names that the App lets a downloaded zip turn into a path. */
public final class SafeNames {
    private SafeNames() {}

    /** One plain file name: no separator, drive colon, NUL, and not "." or "..". */
    public static boolean isFileName(String name) {
        return !name.isEmpty() && !name.equals(".") && !name.equals("..") && name.indexOf('/') < 0
                && name.indexOf('\\') < 0 && name.indexOf(':') < 0 && name.indexOf('\0') < 0;
    }
}
