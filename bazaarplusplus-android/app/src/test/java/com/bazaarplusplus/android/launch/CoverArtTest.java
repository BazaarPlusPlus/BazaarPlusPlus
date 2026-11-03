package com.bazaarplusplus.android.launch;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

import org.junit.Test;

public class CoverArtTest {
    private static final String HOST = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1617400/";

    @Test
    public void theHeroAndThePromotionalArtComeFromTheStoreAssetFormat() throws Exception {
        CoverArt.Urls urls = CoverArt.urls("{\"response\":{\"store_items\":[{\"success\":1,\"assets\":{"
                + "\"asset_url_format\":\"steam/apps/1617400/${FILENAME}?t=1\","
                + "\"library_hero\":\"a/library_hero.jpg\",\"header\":\"b/header.jpg\","
                + "\"hero_capsule_2x\":\"c/hero_capsule_2x.jpg\",\"library_capsule_2x\":\"d/library_capsule_2x.jpg\"}}]}}");
        assertEquals(HOST + "a/library_hero.jpg?t=1", urls.hero);
        assertEquals(HOST + "c/hero_capsule_2x.jpg?t=1", urls.promo);
    }

    @Test
    public void eachImageFallsBackToTheNextStoreAsset() throws Exception {
        CoverArt.Urls urls = CoverArt.urls("{\"response\":{\"store_items\":[{\"success\":1,\"assets\":{"
                + "\"header\":\"b/header.jpg\",\"library_capsule\":\"d/library_capsule.jpg\"}}]}}");
        assertEquals(HOST + "b/header.jpg", urls.hero);
        assertEquals(HOST + "d/library_capsule.jpg", urls.promo);
    }

    @Test
    public void noArtWithoutASuccessfulItem() throws Exception {
        assertNull(CoverArt.urls("{\"response\":{\"store_items\":[{\"success\":0}]}}"));
        assertNull(CoverArt.urls("{\"response\":{}}"));
    }

    @Test
    public void theArtIsHalvedOnlyWhileItStaysTallEnough() {
        assertEquals(1, CoverArt.sampleSize(1240, 1440));
        assertEquals(1, CoverArt.sampleSize(1240, 700));
        assertEquals(2, CoverArt.sampleSize(1240, 600));
        assertEquals(4, CoverArt.sampleSize(2480, 600));
    }
}
