package com.bazaarplusplus.android.launch;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

/**
 * Stands in for the game Activity when the system recreates it in a process that never prepared
 * the game (ADR 0012: after the process was killed). It is never declared: GameComponentFactory
 * instantiates it for the game Activity's component, and it hands over to the launcher at once,
 * so the gate runs again before the game starts.
 */
@SuppressLint("Registered")
public final class RestoreActivity extends Activity {
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        startActivity(new Intent(this, LauncherActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK));
        finish();
    }
}
