package com.bazaarplusplus.android.launch;

import android.content.Context;
import android.graphics.Matrix;
import android.graphics.drawable.Drawable;
import android.util.AttributeSet;
import android.widget.ImageView;

import com.bazaarplusplus.android.R;

/**
 * The launcher's cover (design/components/Launcher): the art cropped to fill it from the top
 * centre, plain bg-hover until the art loads. Laid out by weight, it takes whatever height the
 * layout gives it; measured freely (the page scrolls), it is half as tall as it is wide.
 */
public final class CoverView extends ImageView {
    public CoverView(Context context) {
        this(context, null);
    }

    public CoverView(Context context, AttributeSet attrs) {
        super(context, attrs);
        setScaleType(ScaleType.MATRIX);
        setBackgroundColor(context.getColor(R.color.bpp_bg_hover));
        setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
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

    @Override
    public void setImageDrawable(Drawable drawable) {
        super.setImageDrawable(drawable);
        crop();
    }

    /** Scales the art to cover the view, centred across and anchored at the top. */
    private void crop() {
        Drawable art = getDrawable();
        int width = getWidth();
        int height = getHeight();
        if (art == null || width == 0 || height == 0 || art.getIntrinsicWidth() <= 0) return;
        float scale = Math.max((float) width / art.getIntrinsicWidth(), (float) height / art.getIntrinsicHeight());
        Matrix matrix = new Matrix();
        matrix.setScale(scale, scale);
        matrix.postTranslate((width - art.getIntrinsicWidth() * scale) / 2f, 0);
        setImageMatrix(matrix);
    }
}
