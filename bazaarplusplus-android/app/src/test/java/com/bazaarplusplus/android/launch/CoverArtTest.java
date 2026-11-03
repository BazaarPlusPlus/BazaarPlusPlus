package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

import org.junit.Test;

public class CoverArtTest {
    @Test
    public void theHeroComesFromTheStoreAssetFormat() throws Exception {
        String response = "{\"response\":{\"store_items\":[{\"success\":1,\"assets\":{"
                + "\"asset_url_format\":\"steam/apps/1617400/abc123/${FILENAME}\","
                + "\"library_hero\":\"library_hero.jpg\",\"header\":\"header.jpg\"}}]}}";
        assertEquals("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1617400/abc123/library_hero.jpg",
                CoverArt.heroUrl(response));
    }

    @Test
    public void noArtWithoutASuccessfulItem() throws Exception {
        assertNull(CoverArt.heroUrl("{\"response\":{\"store_items\":[{\"success\":0}]}}"));
        assertNull(CoverArt.heroUrl("{\"response\":{}}"));
    }

    @Test
    public void theArtIsHalvedOnlyWhileItStaysTallEnough() {
        assertEquals(1, CoverArt.sampleSize(1240, 1440));
        assertEquals(1, CoverArt.sampleSize(1240, 700));
        assertEquals(2, CoverArt.sampleSize(1240, 600));
        assertEquals(4, CoverArt.sampleSize(2480, 600));
    }
}
