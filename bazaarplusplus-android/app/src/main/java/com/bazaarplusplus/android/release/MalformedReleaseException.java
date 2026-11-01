package com.bazaarplusplus.android.release;

/** A manifest or Pre-Adaptation record that does not have the shape ADR 0012 defines. */
public final class MalformedReleaseException extends Exception {
    public MalformedReleaseException(String message) {
        super(message);
    }

    public MalformedReleaseException(String message, Throwable cause) {
        super(message, cause);
    }
}
