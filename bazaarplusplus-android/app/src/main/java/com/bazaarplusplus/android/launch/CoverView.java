package com.bazaarplusplus.android.launch;

import android.content.Context;
import android.util.AttributeSet;
import android.widget.FrameLayout;

/** The game card's cover: always twice as wide as it is tall (design/components/Launcher). */
public final class CoverView extends FrameLayout {
    public CoverView(Context context) {
        super(context);
    }

    public CoverView(Context context, AttributeSet attrs) {
        super(context, attrs);
    }

    public CoverView(Context context, AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);
    }

    @Override
    protected void onMeasure(int widthMeasureSpec, int heightMeasureSpec) {
        int width = MeasureSpec.getSize(widthMeasureSpec);
        super.onMeasure(MeasureSpec.makeMeasureSpec(width, MeasureSpec.EXACTLY),
                MeasureSpec.makeMeasureSpec(width / 2, MeasureSpec.EXACTLY));
    }
}
