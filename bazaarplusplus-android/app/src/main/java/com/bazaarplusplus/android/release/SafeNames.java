package com.bazaarplusplus.android.release;

public final class SafeNames {
    private SafeNames() {}

    public static boolean isFileName(String name) {
        return !name.isEmpty() && !name.equals(".") && !name.equals("..") && name.indexOf('/') < 0
                && name.indexOf('\\') < 0 && name.indexOf(':') < 0 && name.indexOf('\0') < 0;
    }
}
