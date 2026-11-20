package com.bazaarplusplus.android.release;

public final class MalformedReleaseException extends Exception {
    public MalformedReleaseException(String message) {
        super(message);
    }

    public MalformedReleaseException(String message, Throwable cause) {
        super(message, cause);
    }
}
