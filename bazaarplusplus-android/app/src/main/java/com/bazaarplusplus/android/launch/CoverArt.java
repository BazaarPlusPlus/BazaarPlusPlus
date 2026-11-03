package com.bazaarplusplus.android.launch;

import android.content.res.Resources;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import com.bazaarplusplus.android.R;

/**
 * The launcher's cover art (design/components/Launcher, Cover art): two bundled images of The
 * Bazaar, which {@code design/build.mjs} copies from {@code design/assets/Covers/}. The library
 * hero (the hunter) is wide; the hero capsule (the store's promotional art, with the logo) is
 * portrait; {@link CoverView} picks one by the cover's shape.
 */
final class CoverArt {
    /** The two images. */
    static final class Images {
        final Bitmap hero;
        final Bitmap promo;

        Images(Bitmap hero, Bitmap promo) {
            this.hero = hero;
            this.promo = promo;
        }
    }

    private CoverArt() {}

    /**
     * Both images, each decoded at least {@code minHeight} pixels tall where the art allows. Decoding
     * takes a while; call it off the main thread.
     */
    static Images load(Resources resources, int minHeight) {
        return new Images(decode(resources, R.drawable.library_hero, minHeight),
                decode(resources, R.drawable.hero_capsule_2x, minHeight));
    }

    /** Decodes no larger than needed: halves the art while it stays at least {@code minHeight} tall. */
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
