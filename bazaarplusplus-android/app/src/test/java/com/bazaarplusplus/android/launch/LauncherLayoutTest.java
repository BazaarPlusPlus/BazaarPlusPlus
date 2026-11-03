package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

public class LauncherLayoutTest {
    private static LauncherLayout layout(int width, int height, boolean largeText) {
        return LauncherLayout.of(width, height, largeText, 600, 840);
    }

    @Test
    public void narrowWindowsAreOneColumn() {
        assertEquals(LauncherLayout.COLUMN, layout(412, 915, false));
        assertEquals(LauncherLayout.COLUMN, layout(599, 900, false));
    }

    @Test
    public void portraitWindowsKeepTheCoverOnTopAtAnyWidth() {
        assertEquals(LauncherLayout.PANES, layout(669, 951, false));
        assertEquals(LauncherLayout.PANES, layout(884, 1104, false));
        assertEquals(LauncherLayout.PANES, layout(1024, 1366, false));
        assertEquals(LauncherLayout.PANES, layout(900, 900, false));
    }

    @Test
    public void onlyAWideWindowWiderThanTallGetsTheCoverPane() {
        assertEquals(LauncherLayout.WIDE, layout(884, 700, false));
        assertEquals(LauncherLayout.PANES, layout(800, 600, false));
    }

    @Test
    public void accessibilityTextSizesKeepOneColumn() {
        assertEquals(LauncherLayout.COLUMN, layout(884, 700, true));
        assertEquals(LauncherLayout.COLUMN, layout(669, 951, true));
    }
}
