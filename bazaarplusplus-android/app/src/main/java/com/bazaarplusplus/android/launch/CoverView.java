package com.bazaarplusplus.android.launch;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Matrix;
import android.util.AttributeSet;
import android.widget.ImageView;

import com.bazaarplusplus.android.R;

/**
 * The launcher's cover (design/components/Launcher, Cover art). A wide cover (short, when the panel
 * takes most of the window) shows the library hero cropped around the hunter's face; any other
 * cover (phones, the cover pane from cover-pane-breakpoint) shows the promotional hero capsule
 * from its top, so the logo stays in view. Plain bg-hover until the art decodes. Laid out by
 * weight, it takes whatever height the layout gives it; measured freely (the page scrolls), it is
 * half as tall as it is wide.
 */
public final class CoverView extends ImageView {
    /** Width over height from which a cover counts as wide. */
    static final float WIDE = 1.2f;
    /** The hunter's face in the current library hero, as fractions of its width and height. */
    static final float HERO_FOCUS_X = 0.70f;
    static final float HERO_FOCUS_Y = 0.30f;
    /** The centre of the logo in the hero capsule, as a fraction of its width. */
    static final float PROMO_FOCUS_X = 0.53f;

    private CoverArt.Images art;
    private Bitmap shown;

    public CoverView(Context context) {
        this(context, null);
    }

    public CoverView(Context context, AttributeSet attrs) {
        super(context, attrs);
        setScaleType(ScaleType.MATRIX);
        setBackgroundColor(context.getColor(R.color.bpp_bg_hover));
        setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
    }

    void setArt(CoverArt.Images art) {
        this.art = art;
        crop();
    }

    @Override
    protected void onMeasure(int widthMeasureSpec, int heightMeasureSpec) {
        int width = MeasureSpec.getSize(widthMeasureSpec);
        int height = MeasureSpec.getMode(heightMeasureSpec) == MeasureSpec.EXACTLY
                ? MeasureSpec.getSize(heightMeasureSpec) : width / 2;
        setMeasuredDimension(width, height);
    }

    @Override
    protected boolean setFrame(int left, int top, int right, int bottom) {
        boolean changed = super.setFrame(left, top, right, bottom);
        crop();
        return changed;
    }

    /** Picks the image for the cover's shape and scales it to cover the view around its focus. */
    private void crop() {
        int width = getWidth();
        int height = getHeight();
        if (art == null || width == 0 || height == 0) return;
        boolean wide = (float) width / height >= WIDE;
        Bitmap image = wide ? art.hero : art.promo;
        if (image != shown) {
            shown = image;
            setImageBitmap(image);
        }
        setImageMatrix(focus(image.getWidth(), image.getHeight(), width, height,
                wide ? HERO_FOCUS_X : PROMO_FOCUS_X, wide ? HERO_FOCUS_Y : 0f));
    }

    /**
     * Scales an image to cover the view and moves its focus as near the view's centre as the
     * image's edges allow.
     */
    static Matrix focus(int imageWidth, int imageHeight, int width, int height, float focusX, float focusY) {
        float scale = Math.max((float) width / imageWidth, (float) height / imageHeight);
        float scaledWidth = imageWidth * scale;
        float scaledHeight = imageHeight * scale;
        Matrix matrix = new Matrix();
        matrix.setScale(scale, scale);
        matrix.postTranslate(clamp(width / 2f - focusX * scaledWidth, width - scaledWidth),
                clamp(height / 2f - focusY * scaledHeight, height - scaledHeight));
        return matrix;
    }

    private static float clamp(float offset, float min) {
        return Math.max(min, Math.min(0, offset));
    }
}
