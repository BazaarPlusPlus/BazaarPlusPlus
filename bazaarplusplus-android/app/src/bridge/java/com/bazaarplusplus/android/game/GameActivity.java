package com.bazaarplusplus.android.game;

import android.content.Context;

/**
 * Lives in bridge.dex, which the game class loader defines together with the game's APKs, so
 * its superclass resolves to the installed game's UnityPlayerActivity. It reaches the App's code
 * through the loader's parent by name, because bridge.dex is compiled apart from the App.
 */
public class GameActivity extends com.unity3d.player.UnityPlayerActivity {
    static final String SESSION = "com.bazaarplusplus.android.game.GameSession";

    @Override
    protected void attachBaseContext(Context base) {
        Context wrapped;
        try {
            wrapped = (Context) Class.forName(SESSION).getMethod("wrap", Context.class).invoke(null, base);
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException("Cannot reach " + SESSION, e);
        }
        super.attachBaseContext(wrapped);
    }
}
