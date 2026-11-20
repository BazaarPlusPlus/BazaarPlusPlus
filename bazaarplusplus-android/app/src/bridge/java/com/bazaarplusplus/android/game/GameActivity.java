package com.bazaarplusplus.android.game;

import android.content.Context;

/** The game class loader defines this bridge so its superclass resolves to the installed game's UnityPlayerActivity. */
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
