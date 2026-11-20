package com.bazaarplusplus.android.launch;

enum LauncherLayout {
    COLUMN, PANES, WIDE;

    /** A square window keeps the cover on top because the side pane would crop the logo. */
    static LauncherLayout of(int width, int height, boolean largeText, float paneBreakpoint, float coverPaneBreakpoint) {
        if (largeText || width < paneBreakpoint) return COLUMN;
        return width < coverPaneBreakpoint || height >= width ? PANES : WIDE;
    }
}
