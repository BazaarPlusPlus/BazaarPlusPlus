package com.bazaarplusplus.android.game;

import static org.junit.Assert.assertEquals;

import android.content.ContextWrapper;

import org.junit.Test;

import java.io.File;

public class GameStorageTest {
    @Test
    public void theRootIsInInternalStorage() {
        File internal = new File("/data/user/0/app/no_backup");
        ContextWrapper context = new ContextWrapper(null) {
            @Override public File getNoBackupFilesDir() { return internal; }
        };
        assertEquals(new File(internal, "game-external"), GameStorage.root(context));
    }
}
