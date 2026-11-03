package com.bazaarplusplus.android.launch;

/** The launcher's three layouts and the window each suits (design/components/Launcher, Layout). */
enum LauncherLayout {
    COLUMN, PANES, WIDE;

    /**
     * The layout for a window, in dp. A window as tall as it is wide keeps the cover on top: half of
     * it would be too narrow to show the logo.
     */
    static LauncherLayout of(int width, int height, boolean largeText, float paneBreakpoint, float coverPaneBreakpoint) {
        if (largeText || width < paneBreakpoint) return COLUMN;
        return width < coverPaneBreakpoint || height >= width ? PANES : WIDE;
    }
}
