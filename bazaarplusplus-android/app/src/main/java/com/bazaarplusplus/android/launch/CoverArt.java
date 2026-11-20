package com.bazaarplusplus.android.launch;

import android.content.res.Resources;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import com.bazaarplusplus.android.R;

final class CoverArt {
    static final class Images {
        final Bitmap hero;
        final Bitmap promo;

        Images(Bitmap hero, Bitmap promo) {
            this.hero = hero;
            this.promo = promo;
        }
    }

    private CoverArt() {}

    static Images load(Resources resources, int minHeight) {
        return new Images(decode(resources, R.drawable.library_hero, minHeight),
                decode(resources, R.drawable.hero_capsule_2x, minHeight));
    }

    private static Bitmap decode(Resources resources, int image, int minHeight) {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        BitmapFactory.decodeResource(resources, image, bounds);
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = sampleSize(bounds.outHeight, minHeight);
        return BitmapFactory.decodeResource(resources, image, options);
    }

    static int sampleSize(int height, int minHeight) {
        int sample = 1;
        while (height / (sample * 2) >= minHeight) sample *= 2;
        return sample;
    }
}
