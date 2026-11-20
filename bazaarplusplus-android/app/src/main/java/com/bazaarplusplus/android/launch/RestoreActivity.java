package com.bazaarplusplus.android.launch;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

// GameComponentFactory substitutes this Activity for a restored game Activity in an unprepared process.
// The manifest declares the game component, so lint needs the Registered suppression here.
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
