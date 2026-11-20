package com.bazaarplusplus.android;

import android.app.Activity;
import android.app.AppComponentFactory;
import android.content.Intent;
import android.util.Log;

import com.bazaarplusplus.android.game.GameSession;
import com.bazaarplusplus.android.launch.RestoreActivity;

public final class GameComponentFactory extends AppComponentFactory {
    @Override
    public Activity instantiateActivity(ClassLoader cl, String className, Intent intent)
            throws InstantiationException, IllegalAccessException, ClassNotFoundException {
        if (!GameSession.GAME_ACTIVITY.equals(className)) {
            return super.instantiateActivity(cl, className, intent);
        }
        if (!GameSession.isPrepared()) {
            Log.i(BppApplication.TAG, "restoring " + className + " without a prepared game: opening the launcher");
            return new RestoreActivity();
        }
        ClassLoader game = GameSession.loader();
        if (intent != null) intent.setExtrasClassLoader(game);
        Log.i(BppApplication.TAG, "instantiating " + className + " from the game loader");
        try {
            return (Activity) game.loadClass(className).getDeclaredConstructor().newInstance();
        } catch (ReflectiveOperationException e) {
            InstantiationException wrapped = new InstantiationException(e.toString());
            wrapped.initCause(e);
            throw wrapped;
        }
    }
}
