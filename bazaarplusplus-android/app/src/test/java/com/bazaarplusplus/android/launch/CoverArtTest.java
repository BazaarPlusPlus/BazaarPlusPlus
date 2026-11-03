package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

public class CoverArtTest {
    @Test
    public void theArtIsHalvedOnlyWhileItStaysTallEnough() {
        assertEquals(1, CoverArt.sampleSize(1240, 1440));
        assertEquals(1, CoverArt.sampleSize(1240, 700));
        assertEquals(2, CoverArt.sampleSize(1240, 600));
        assertEquals(4, CoverArt.sampleSize(2480, 600));
    }
}
