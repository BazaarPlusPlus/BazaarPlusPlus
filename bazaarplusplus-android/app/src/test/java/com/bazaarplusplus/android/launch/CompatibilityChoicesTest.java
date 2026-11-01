package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import com.bazaarplusplus.android.game.GameCodeHash;

import org.junit.Rule;
import org.junit.Test;
import org.junit.rules.TemporaryFolder;

import java.io.File;

public class CompatibilityChoicesTest {
    @Rule public TemporaryFolder tmp = new TemporaryFolder();

    @Test
    public void theChoiceIsKeptPerGameCodeHash() throws Exception {
        File file = new File(tmp.getRoot(), "compatibility.json");
        GameCodeHash first = new GameCodeHash("a".repeat(64), "b".repeat(64));
        GameCodeHash second = new GameCodeHash("a".repeat(64), "c".repeat(64));
        CompatibilityChoices choices = new CompatibilityChoices(file);
        assertFalse(choices.chosen(first));
        choices.choose(first);
        assertTrue(new CompatibilityChoices(file).chosen(first));
        assertFalse("another game code asks again", new CompatibilityChoices(file).chosen(second));
        choices.choose(second);
        choices.choose(second);
        assertTrue(choices.chosen(first));
        assertTrue(choices.chosen(second));
    }
}
