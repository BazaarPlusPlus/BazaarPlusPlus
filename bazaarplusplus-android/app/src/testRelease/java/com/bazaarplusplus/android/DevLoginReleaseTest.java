package com.bazaarplusplus.android;

import android.content.ContextWrapper;
import org.junit.Test;
import java.io.File;
import static org.junit.Assert.*;

public class DevLoginReleaseTest {
    @Test public void releaseNeverReadsDevelopmentLoginFiles() {
        ContextWrapper context = new ContextWrapper(null) {
            @Override public File getFilesDir() { throw new AssertionError("Release accessed development files"); }
        };
        assertNull(DevOverrides.loginDirectory(context));
        assertFalse(DevOverrides.loginRequested(context));
        DevOverrides.rejectLogin(context, "WAITING");
    }
}
