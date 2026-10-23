#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Localized labels for the Collection panel UI and its settings-dock entry. Lookups follow
// the HistoryPanelText pattern: store one LocalizedTextSet per concept, resolve through the
// current PlayerPreferences language code, fall through to English when nothing else fits.
public static class CollectionPanelText
{
    private static readonly LocalizedTextSet TitleText = new("Card Collection", "图鉴", "圖鑑");

    private static readonly LocalizedTextSet SubtitleText = new(
        "Supported by the BazaarPlusPlus community.",
        "由 BazaarPlusPlus 玩家社区支持。",
        "由 BazaarPlusPlus 玩家社群支持。"
    );

    private static readonly LocalizedTextSet ItemsTabText = new("Items", "物品", "物品");
    private static readonly LocalizedTextSet SkillsTabText = new("Skills", "技能", "技能");
    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉");
    private static readonly LocalizedTextSet ResetFiltersText = new(
        "Reset filters",
        "重置筛选",
        "重設篩選"
    );
    private static readonly LocalizedTextSet SearchTooltipText = new(
        "Search names, descriptions, internal ids, tags, and related effects.",
        "搜索名称、描述、内部 ID、标签和相关效果。",
        "搜尋名稱、描述、內部 ID、標籤和相關效果。"
    );
    private static readonly LocalizedTextSet ItemSearchPlaceholderText = new(
        "Search items",
        "搜索物品",
        "搜尋物品"
    );
    private static readonly LocalizedTextSet SkillSearchPlaceholderText = new(
        "Search skills",
        "搜索技能",
        "搜尋技能"
    );

    private static readonly LocalizedTextSet HeroHeaderText = new("Basic", "基础", "基礎");
    private static readonly LocalizedTextSet AllHeroesTooltipText = new(
        "Show cards for every hero",
        "显示所有英雄的卡牌",
        "顯示所有英雄的卡牌"
    );
    private static readonly LocalizedTextSet DayHeaderText = new("Day", "天数", "天數");
    private static readonly LocalizedTextSet DayCaptionText = new(
        "DAY",
        "<line-height=0.9em>天数</line-height>",
        "<line-height=0.9em>天數</line-height>"
    );
    private static readonly LocalizedTextSet TierSizeHeaderText = new(
        "Size / Quality",
        "尺寸 / 品质",
        "尺寸 / 品質"
    );
    private static readonly LocalizedTextSet TierHeaderText = new("Quality", "品质", "品質");
    private static readonly LocalizedTextSet TagHeaderText = new("Types", "类型", "類型");
    private static readonly LocalizedTextSet KeywordHeaderText = new("Tags", "标签", "標籤");
    private static readonly LocalizedTextSet FacetMatchAnyText = new("Any", "任一", "任一");
    private static readonly LocalizedTextSet FacetMatchAllText = new("All", "全部", "全部");
    private static readonly LocalizedTextSet SortQualityText = new("Quality", "品质", "品質");
    private static readonly LocalizedTextSet SortSizeText = new("Size", "尺寸", "尺寸");
    private static readonly LocalizedTextSet MerchantHeaderText = new("Merchant", "商人", "商人");
    private static readonly LocalizedTextSet TrainerHeaderText = new("Trainer", "训练师", "訓練師");
    private static readonly LocalizedTextSet CatalogLoadingText = new(
        "Loading card data...",
        "正在加载卡牌数据...",
        "正在載入卡牌資料..."
    );
    private static readonly LocalizedTextSet CatalogUnavailableText = new(
        "Card data is unavailable right now. Try opening from the main menu.",
        "暂时无法读取卡牌数据，请稍后或在主菜单中重试。",
        "暫時無法讀取卡牌資料，請稍後或在主選單中重試。"
    );
    private static readonly LocalizedTextSet NoMatchesText = new(
        "No cards match the current filters.",
        "没有符合当前筛选条件的卡。",
        "沒有符合目前篩選條件的卡。"
    );
    private static readonly LocalizedTextSet SourceDisclaimerText = new(
        "Source filters are inferred from card types; in-game offers remain authoritative.",
        "来源筛选基于类型推导，具体以游戏内实际为准。",
        "來源篩選基於類型推導，具體以遊戲內實際為準。"
    );

    public static string Title() => Resolve(TitleText);

    public static string Subtitle() => Resolve(SubtitleText);

    public static string ItemsTab() => Resolve(ItemsTabText);

    public static string SkillsTab() => Resolve(SkillsTabText);

    public static string Close() => Resolve(CloseText);

    public static string ResetFilters() => Resolve(ResetFiltersText);

    public static string SearchTooltip() => Resolve(SearchTooltipText);

    public static string SearchPlaceholder(CardType activeType) =>
        activeType == CardType.Skill
            ? Resolve(SkillSearchPlaceholderText)
            : Resolve(ItemSearchPlaceholderText);

    public static string HeroHeader() => Resolve(HeroHeaderText);

    public static string AllHeroesTooltip() => Resolve(AllHeroesTooltipText);

    public static string DayHeader() => Resolve(DayHeaderText);

    public static string DayCaption() => Resolve(DayCaptionText);

    public static string TierSizeHeader() => Resolve(TierSizeHeaderText);

    public static string TierHeader() => Resolve(TierHeaderText);

    public static string TagHeader() => Resolve(TagHeaderText);

    public static string KeywordHeader() => Resolve(KeywordHeaderText);

    public static string FacetMatchMode(CollectionFacetMatchMode mode) =>
        mode == CollectionFacetMatchMode.All
            ? Resolve(FacetMatchAllText)
            : Resolve(FacetMatchAnyText);

    public static string SortQuality() => Resolve(SortQualityText);

    public static string SortSize() => Resolve(SortSizeText);

    public static string SourceHeader(CardType activeType) =>
        activeType == CardType.Skill ? Resolve(TrainerHeaderText) : Resolve(MerchantHeaderText);

    public static string CatalogLoading() => Resolve(CatalogLoadingText);

    public static string CatalogUnavailable() => Resolve(CatalogUnavailableText);

    public static string NoMatches() => Resolve(NoMatchesText);

    public static string SourceDisclaimer() => Resolve(SourceDisclaimerText);

    public static string Tier(CardTier tier)
    {
        var (english, chineseMainland, chineseTraditional) = TierForms(tier);
        return chineseMainland.Length == 0
            ? english
            : FormatSimple(english, chineseMainland, chineseTraditional);
    }

    // Game-authored text follows the game language's script, not the BPP locale mode,
    // so matchers against game text need both Chinese variants at once.
    public static (string English, string ChineseMainland, string ChineseTraditional) TierForms(
        CardTier tier
    ) =>
        tier switch
        {
            CardTier.Bronze => ("Bronze", "青铜", "青銅"),
            CardTier.Silver => ("Silver", "白银", "白銀"),
            CardTier.Gold => ("Gold", "黄金", "黃金"),
            CardTier.Diamond => ("Diamond", "钻石", "鑽石"),
            CardTier.Legendary => ("Legendary", "传说", "傳說"),
            _ => (tier.ToString(), string.Empty, string.Empty),
        };

    public static string Size(CardSize size) =>
        size switch
        {
            CardSize.Small => FormatSimple("Small", "小型", "小型"),
            CardSize.Medium => FormatSimple("Medium", "中型", "中型"),
            CardSize.Large => FormatSimple("Large", "大型", "大型"),
            _ => size.ToString(),
        };

    public static bool IsChineseLanguage() => LanguageCodeMatcher.IsChinese(L.CurrentLanguageCode);

    // Tag labels intentionally have no entry here: chips resolve through the game's native
    // typography (GameInterop.TagTypography.NativeTagTypography), never a mod-side dictionary.

    // The Dragons' label is the game's own localized name, so the host supplies it.
    public static string Hero(CardHero hero, Func<string> theDragonsDisplayName)
    {
        if (hero == CardHero.TheDragons)
            return theDragonsDisplayName();

        return hero switch
        {
            CardHero.Common => FormatSimple("Common", "通用", "通用"),
            CardHero.Vanessa => FormatSimple("Vanessa", "Vanessa", "Vanessa"),
            CardHero.Pygmalien => FormatSimple("Pygmalien", "Pygmalien", "Pygmalien"),
            CardHero.Dooley => FormatSimple("Dooley", "Dooley", "Dooley"),
            CardHero.Mak => FormatSimple("Mak", "Mak", "Mak"),
            CardHero.Jules => FormatSimple("Jules", "Jules", "Jules"),
            CardHero.Karnok => FormatSimple("Karnok", "Karnok", "Karnok"),
            CardHero.Stelle => FormatSimple("Stelle", "Stelle", "Stelle"),
            _ => hero.ToString(),
        };
    }

    private static string Resolve(LocalizedTextSet set) => L.Resolve(set);

    private static string FormatSimple(
        string english,
        string chineseMainland,
        string chineseTraditional
    )
    {
        return L.Resolve(new LocalizedTextSet(english, chineseMainland, chineseTraditional));
    }
}
