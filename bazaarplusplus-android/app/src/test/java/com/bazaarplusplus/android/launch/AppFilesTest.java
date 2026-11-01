package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;

import org.json.JSONObject;
import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

public class AppFilesTest {
    /** The mod's mirror of PathConstants.DataRootDirectoryName (root AGENTS.md, Contracts); unit tests run in app/. */
    static final File HISTORY_DATABASE = new File(
            "../../bazaarplusplus-mod/src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json");

    @Test
    public void theDataRootIsTheModsDataRoot() throws Exception {
        JSONObject contract = new JSONObject(new String(Files.readAllBytes(HISTORY_DATABASE.toPath()),
                StandardCharsets.UTF_8));
        assertEquals(contract.getString("dataRootDirectoryName"), AppFiles.DATA_ROOT);
    }
}
