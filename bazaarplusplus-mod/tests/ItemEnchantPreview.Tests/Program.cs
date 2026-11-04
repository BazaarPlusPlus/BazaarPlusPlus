using BazaarGameClient.Domain.Models.Cards;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Patches.Tooltips;
using TheBazaar.Tooltips;

Assert(
    TooltipPreviewContentRefresh.ResolveUpgradePreviewTransition(
        canUpgrade: false,
        TooltipPreviewMode.Upgrade,
        TooltipPreviewMode.Normal
    ) == TooltipUpgradePreviewTransition.Exit,
    "Leaving upgrade mode must clear the controller preview even if the card can no longer upgrade."
);
Assert(
    TooltipPreviewContentRefresh.ResolveUpgradePreviewTransition(
        canUpgrade: false,
        TooltipPreviewMode.Normal,
        TooltipPreviewMode.Upgrade
    ) == TooltipUpgradePreviewTransition.None,
    "A card that cannot upgrade must not enter upgrade preview."
);
Assert(
    TooltipPreviewContentRefresh.ResolveUpgradePreviewTransition(
        canUpgrade: true,
        TooltipPreviewMode.Enchant,
        TooltipPreviewMode.Upgrade
    ) == TooltipUpgradePreviewTransition.Enter,
    "An upgradeable card should enter upgrade preview when Shift takes priority."
);
Assert(
    TooltipPreviewContentRefresh.ResolveUpgradePreviewTransition(
        canUpgrade: true,
        TooltipPreviewMode.Upgrade,
        TooltipPreviewMode.Upgrade
    ) == TooltipUpgradePreviewTransition.None,
    "An unchanged mode must not replay the preview transition."
);

Assert(
    UpgradeTooltipScheduler.ShouldRefreshUpgradePreview(
        controllerStillOwnsCard: true,
        isCursorOverCard: true,
        previewActive: true
    ),
    "An active upgrade preview should refresh while the cursor still owns the same card."
);
Assert(
    !UpgradeTooltipScheduler.ShouldRefreshUpgradePreview(
        controllerStillOwnsCard: true,
        isCursorOverCard: false,
        previewActive: true
    ),
    "Toggle mode must not refresh a tooltip after the cursor has left the card."
);
Assert(
    !UpgradeTooltipScheduler.ShouldRefreshUpgradePreview(
        controllerStillOwnsCard: false,
        isCursorOverCard: true,
        previewActive: true
    ),
    "A recycled controller must not refresh the card that originally scheduled the coroutine."
);
Assert(
    !UpgradeTooltipScheduler.ShouldRefreshUpgradePreview(
        controllerStillOwnsCard: true,
        isCursorOverCard: true,
        previewActive: false
    ),
    "A disabled upgrade preview must stop a pending hover refresh."
);

Assert(
    UpgradePreviewCardLatch.ShouldReleaseLatch(isCursorOverCard: false, previewActive: true),
    "Toggle mode must release the upgrade visual once the cursor leaves the card that entered it."
);
Assert(
    UpgradePreviewCardLatch.ShouldReleaseLatch(isCursorOverCard: true, previewActive: false),
    "Deactivating upgrade preview must release every latched card visual."
);
Assert(
    !UpgradePreviewCardLatch.ShouldReleaseLatch(isCursorOverCard: true, previewActive: true),
    "The card under the cursor keeps its upgrade visual while preview stays active."
);

var candidates = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo]
);

Assert(
    candidates.SequenceEqual([EEnchantmentType.Icy, EEnchantmentType.Turbo]),
    "Candidate selection should dedupe inputs and exclude the current enchantment."
);

var restrictedToOffered = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo],
    restrictToEnchantmentNames: ["icy"]
);

Assert(
    restrictedToOffered.SequenceEqual([EEnchantmentType.Icy]),
    "A pedestal restriction should keep only the offered enchant type(s), matched case-insensitively."
);

var emptyRestriction = ItemEnchantPreviewCandidateSelector.SelectCandidates(
    currentEnchantment: EEnchantmentType.Heavy,
    allEnchantments: [EEnchantmentType.Heavy, EEnchantmentType.Icy, EEnchantmentType.Turbo],
    restrictToEnchantmentNames: []
);

Assert(
    emptyRestriction.SequenceEqual([EEnchantmentType.Icy, EEnchantmentType.Turbo]),
    "An empty restriction (manual Ctrl / Always / empty random pool) should keep the full candidate list."
);

Assert(
    ItemEnchantPreviewEligibility.IsEligible(
        ECardType.Item,
        EInventorySection.Hand,
        isInCombat: false
    ),
    "Items in hand should be eligible for enchant previews."
);

Assert(
    !ItemEnchantPreviewEligibility.IsEligible(ECardType.Item, section: null, isInCombat: false),
    "Items outside hand or stash should not be eligible."
);

Assert(
    !ItemEnchantPreviewEligibility.IsEligible(
        ECardType.Item,
        EInventorySection.Hand,
        isInCombat: true
    ),
    "Combat state should suppress enchant previews."
);

Assert(
    !BppTooltipSectionRenderPatch.CanRenderEnchantPreview(isInCombat: false, isRecapViewOpen: true),
    "Recap tooltips should suppress the appended enchant preview section."
);
Assert(
    BppTooltipSectionRenderPatch.CanRenderEnchantPreview(isInCombat: false, isRecapViewOpen: false),
    "Ordinary out-of-combat tooltips should retain enchant previews."
);

var opponentBoardItem = new ItemCard { Type = ECardType.Item, Section = null };

Assert(
    ItemEnchantPreviewEligibility.IsEligible(opponentBoardItem, isInCombat: false),
    "Opponent-board items without inventory ownership should still be eligible for enchant previews."
);

var itemCard = new ItemCard
{
    Type = ECardType.Item,
    Section = EInventorySection.Hand,
    Enchantment = EEnchantmentType.Heavy,
    Attributes = { [ECardAttributeType.DamageAmount] = 10, [ECardAttributeType.Cooldown] = 4000 },
};

var previewTemplate = new TEnchantment
{
    Attributes =
    {
        [ECardAttributeType.DamageAmount] = 15,
        [ECardAttributeType.FreezeAmount] = 2000,
    },
};

var snapshot = ItemEnchantPreviewSnapshotFactory.Create(
    itemCard,
    EEnchantmentType.Icy,
    previewTemplate
);

Assert(
    snapshot.CurrentEnchantment == EEnchantmentType.Heavy,
    "Snapshot should retain the source card's current enchantment."
);

Assert(
    snapshot.PreviewEnchantment == EEnchantmentType.Icy,
    "Snapshot should track the target preview enchantment."
);

Assert(
    snapshot.PreviewAttributes[ECardAttributeType.DamageAmount] == 15,
    "Preview attributes should override the item's base attributes."
);

Assert(
    snapshot.PreviewAttributes[ECardAttributeType.Cooldown] == 4000,
    "Preview attributes should preserve unchanged item attributes."
);

Assert(
    snapshot.PreviewAttributes[ECardAttributeType.FreezeAmount] == 2000,
    "Preview attributes should include attributes introduced by the preview enchantment."
);

var previewCard = ItemEnchantPreviewCardCloneFactory.Create(itemCard, snapshot);
previewCard.Attributes[ECardAttributeType.DamageAmount] = 42;

Assert(
    previewCard.Enchantment == EEnchantmentType.Icy,
    "Preview clone should expose the target enchantment."
);

Assert(
    itemCard.Enchantment == EEnchantmentType.Heavy,
    "Preview clone should not mutate the source card's enchantment."
);

Assert(
    itemCard.Attributes[ECardAttributeType.DamageAmount] == 10,
    "Preview clone should not share attribute state with the source card."
);

Assert(
    ItemEnchantPreviewFormatting.GetEnchantmentColorHex(EEnchantmentType.Icy) == "3FC8F7",
    "Formatting should expose the configured enchantment color."
);

Assert(
    typeof(ItemEnchantPreviewFormatting).Assembly.GetType(
        "BazaarPlusPlus.Patches.Tooltips.CardTooltipDataPassivePatch"
    ) == null,
    "Enchant preview must not append into the game's native passive-tooltip string."
);

var questSectionLayout = BppTooltipSectionRenderPatch.ResolveSectionLayout(
    passiveText: "",
    questGroupCount: 1
);
Assert(
    questSectionLayout.SectionKey == BppTooltipSectionRenderPatch.EnchantAfterQuestSectionKey,
    "Quest-only tooltips should use their own cached section layout."
);
Assert(
    questSectionLayout.Style.SectionTopPaddingScale == 0f,
    "Quest rows already provide the native section seam, so the enchant clone must not add top padding."
);
Assert(
    questSectionLayout.Style.SourceBottomPaddingScale == 0f
        && questSectionLayout.Style.QuestGroupBottomPaddingScale == 0f,
    "Quest-only tooltips should remove the two host bottom paddings that stack below the last quest row."
);

var passiveTextSectionLayout = BppTooltipSectionRenderPatch.ResolveSectionLayout(
    passiveText: "Passive effect",
    questGroupCount: 1
);
Assert(
    passiveTextSectionLayout.SectionKey == BppTooltipSectionRenderPatch.EnchantWithNativeSectionKey
        && passiveTextSectionLayout.Style.SectionTopPaddingScale == 1f
        && passiveTextSectionLayout.Style.SourceBottomPaddingScale == 0.5f
        && passiveTextSectionLayout.Style.QuestGroupBottomPaddingScale == null,
    "Passive text should retain its existing native-content spacing even when quest rows also exist."
);

Assert(
    ItemEnchantPreviewTooltipLayerPolicy.ElevatedSortingOrder(150) == 151,
    "Enchant preview elevation should sit one step above the tooltip clone's own sorting order, not an absolute layer."
);

var segment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "Freeze for 2 seconds"
);

Assert(
    segment.Text.Contains("Icy") && segment.Text.Contains("Freeze for 2 seconds"),
    "Formatted segments should contain both the enchantment label and rendered body text."
);
Assert(
    !segment.Text.Contains("·") && !segment.Text.Contains("\u00A0"),
    "Formatted enchantment previews should not reserve bullet or indentation space."
);

var cjkSegment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "<line-height=1.6em>冰冷时获得护盾</line-height>"
);

Assert(
    cjkSegment.Text.Contains("<line-height=1.9em>")
        && !cjkSegment.Text.Contains("<line-height=1.6em>")
        && !cjkSegment.Text.Contains("<line-height=2.1em>"),
    "CJK enchant preview wrapped lines should use the shared line height."
);

var englishLineHeightSegment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "<line-height=1.6em>Freeze for 2 seconds</line-height>"
);

Assert(
    englishLineHeightSegment.Text.Contains("<line-height=1.9em>")
        && !englishLineHeightSegment.Text.Contains("<line-height=1.6em>")
        && !englishLineHeightSegment.Text.Contains("<line-height=2.1em>"),
    "English enchant preview wrapped lines should use the shared line height."
);

var germanLineHeightSegment = ItemEnchantPreviewFormatting.CreateSegment(
    EEnchantmentType.Icy,
    "<line-height=1.6em>Für 2 Sekunden einfrieren</line-height>"
);

Assert(
    germanLineHeightSegment.Text.Contains("<line-height=1.9em>")
        && !germanLineHeightSegment.Text.Contains("<line-height=1.6em>")
        && !germanLineHeightSegment.Text.Contains("<line-height=2.1em>"),
    "Other enchant preview languages should use the shared line height."
);

var sectionText = ItemEnchantPreviewFormatting.BuildSectionText(
    new[] { segment, new TooltipSegment("<size=55%>second\r\n\r\nthird</size>", null, null, -1) }
);
Assert(
    sectionText
        == $"{segment.Text}<size=55%><line-height=2.1em>\n</line-height></size><size=55%>second\n\nthird</size>"
        && !sectionText.EndsWith("\n", StringComparison.Ordinal),
    "Section text should separate entries without flattening an entry's paragraphs or list lines."
);

var cjkSectionText = ItemEnchantPreviewFormatting.BuildSectionText(
    new[] { cjkSegment, cjkSegment }
);
Assert(
    cjkSectionText.Contains("<size=55%><line-height=2.1em>\n</line-height></size>")
        && !cjkSectionText.Contains("<line-height=2.3em>"),
    "CJK enchant preview entries should use the shared entry break."
);

var germanSectionText = ItemEnchantPreviewFormatting.BuildSectionText(
    new[] { germanLineHeightSegment, germanLineHeightSegment }
);
Assert(
    germanSectionText.Contains("<size=55%><line-height=2.1em>\n</line-height></size>")
        && !germanSectionText.Contains("<line-height=2.3em>"),
    "Other enchant preview languages should use the shared entry break."
);

var key1 = ItemEnchantPreviewCache.CreateKey(snapshot, "en");

var changedSnapshot = new ItemEnchantPreviewSnapshot
{
    CurrentEnchantment = snapshot.CurrentEnchantment,
    PreviewEnchantment = snapshot.PreviewEnchantment,
    PreviewAttributes = new Dictionary<ECardAttributeType, int>(snapshot.PreviewAttributes)
    {
        [ECardAttributeType.DamageAmount] = 99,
    },
};

var key2 = ItemEnchantPreviewCache.CreateKey(changedSnapshot, "en");

Assert(key1 != key2, "Cache keys must change when preview-relevant attributes change.");
Assert(
    key1 != ItemEnchantPreviewCache.CreateKey(snapshot, "zh-Hans"),
    "Cache keys must change with the game language, which switches live."
);

// The pedestal catalog names enchantments by EEnchantmentType member name (ADR-0015); the desktop
// restricts its preview by those names, so each must still be a member of the game's enum.
foreach (var name in BazaarPlusPlus.Core.GameState.PedestalEnchantCatalog.EnchantmentTypeNames)
    Assert(
        Enum.TryParse<EEnchantmentType>(name, ignoreCase: false, out _),
        $"The pedestal catalog's enchantment '{name}' must be an EEnchantmentType member."
    );

// iOS host: as on the Android host, a fight's or a replay's card neither previews its upgrade nor
// offers the switch, whatever [Touch] UpgradePreview says. BppHotkeyService.IsActive(HoldUpgradePreview), which
// the tooltip patches read, returns TouchUpgradePreview.Active on a mobile platform.
var touchSettings = new MemorySettingsStore();
var previewBlocked = false;
TouchUpgradePreview.Bind(touchSettings, () => previewBlocked);
touchSettings.Bool("Touch", "UpgradePreview").Value = true;
var shopCard = new object();
TouchUpgradePreview.BeginTooltip(shopCard);
Assert(
    TouchUpgradePreview.Active == true
        && TouchUpgradePreview.Offered
        && TouchUpgradePreview.Holds(shopCard),
    "Outside a fight or a replay a new tooltip starts with the upgrade preview the settings switch turns on."
);
previewBlocked = true;
var replayCard = new object();
TouchUpgradePreview.BeginTooltip(replayCard);
Assert(
    TouchUpgradePreview.Active == false && !TouchUpgradePreview.Holds(replayCard),
    "A fight or replay card's tooltip must not start with the upgrade preview."
);
Assert(
    !TouchUpgradePreview.Offered,
    "A fight or replay card's tooltip must not offer the upgrade switch."
);
previewBlocked = false;
TouchUpgradePreview.BeginTooltip(shopCard);
Assert(
    TouchUpgradePreview.Active == true,
    "After the fight or replay a card's tooltip starts at the settings switch again."
);
TouchUpgradePreview.End();

var savedAbility = new TCardAbility
{
    Id = "saved",
    Action = new TActionCardModifyAttribute
    {
        AttributeType = ECardAttributeType.BurnApplyAmount,
        Target = new TTargetCardSelf(),
        Value = new TReferenceValueCardAttribute
        {
            AttributeType = ECardAttributeType.Custom_1,
            Target = new TTargetCardSelf(),
        },
    },
};
var savedTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Abilities = new Dictionary<string, TCardAbility> { [savedAbility.Id] = savedAbility },
    Enchantments = new Dictionary<EEnchantmentType, TEnchantment>
    {
        [EEnchantmentType.Fiery] = new TEnchantment
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 7,
            },
        },
    },
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Custom_1] = 5,
                [ECardAttributeType.Custom_2] = 6,
            },
        },
    },
};
var templateValues = new ItemCard { Template = savedTemplate, Type = ECardType.Item };
TouchCardText.FillPreviewCard(
    templateValues,
    savedTemplate,
    savedTemplate,
    new CardRentalSubject(savedTemplate.Id, CardTier.Bronze)
);
Assert(
    templateValues.Attributes[ECardAttributeType.Custom_1] == 5,
    "A card with no saved values keeps its template's values."
);
var savedValues = new ItemCard { Template = savedTemplate, Type = ECardType.Item };
TouchCardText.FillPreviewCard(
    savedValues,
    savedTemplate,
    savedTemplate,
    new CardRentalSubject(
        savedTemplate.Id,
        CardTier.Bronze,
        CardEnchantment.Fiery,
        new Dictionary<string, int> { ["Custom_1"] = 25, ["NotAGameAttribute"] = 99 }
    )
);
Assert(
    savedValues.Attributes[ECardAttributeType.Custom_1] == 25,
    "A saved value wins over the template's tier and the enchantment's values."
);
Assert(
    savedValues.Attributes[ECardAttributeType.Custom_2] == 6,
    "A value the card did not save keeps its template's value."
);
Assert(savedValues.Attributes.Count == 2, "A saved name the game does not know is dropped.");
var savedToken = TooltipComponentAbility.Create(
    new TooltipContext(savedValues, savedTemplate, new ValueContext(null!, savedValues)),
    savedAbility.Id,
    null,
    0
);
Assert(
    savedToken?.Resolve() == 25f,
    "The tooltip's own token must resolve the saved value, the number the inspection text shows."
);

var boardValues = new Dictionary<ECardAttributeType, int> { [ECardAttributeType.Custom_1] = 25 };
var boardCard = new ItemCard { Template = savedTemplate, Type = ECardType.Item };

// The steps of CardPreviewBase.SetUp: the client card adopts the instance's dictionary, the
// starting tier writes into it, and the card then takes a new dictionary with the tier's and
// the enchantment's values on top.
Task BoardSetUp(Dictionary<ECardAttributeType, int> instanceValues)
{
    boardCard.Attributes = instanceValues;
    foreach (var pair in savedTemplate.Tiers[ETier.Bronze].Attributes)
        boardCard.Attributes[pair.Key] = pair.Value;
    var attributes = TheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
        boardCard,
        savedTemplate,
        ETier.Bronze
    );
    TheBazaar.CardExtensions.ApplyPreviewEnchantment(
        savedTemplate,
        boardCard,
        attributes,
        EEnchantmentType.Fiery
    );
    boardCard.Attributes = attributes;
    return Task.CompletedTask;
}
var boardWrote = await SavedCardValues.KeepThroughSetUp(
    boardValues,
    () => BoardSetUp(boardValues),
    () => boardCard.Attributes
);
Assert(boardWrote, "A card with saved values reports the write, so its gems are drawn again.");
Assert(
    boardCard.Attributes[ECardAttributeType.Custom_1] == 25,
    "A board card's saved value wins over everything the set-up wrote."
);
Assert(
    boardCard.Attributes[ECardAttributeType.Custom_2] == 6,
    "A board card keeps the template's value for what it did not save."
);
Assert(
    boardValues.Count == 1 && boardValues[ECardAttributeType.Custom_1] == 25,
    "The instance keeps its saved values, so rendering the same instance again shows them."
);
var boardToken = TooltipComponentAbility.Create(
    new TooltipContext(boardCard, savedTemplate, new ValueContext(null!, boardCard)),
    savedAbility.Id,
    null,
    0
);
Assert(boardToken?.Resolve() == 25f, "The board card's tooltip must resolve the saved value.");
var templateBoardValues = new Dictionary<ECardAttributeType, int>();
var templateBoardWrote = await SavedCardValues.KeepThroughSetUp(
    templateBoardValues,
    () => BoardSetUp(templateBoardValues),
    () => boardCard.Attributes
);
Assert(
    !templateBoardWrote && boardCard.Attributes[ECardAttributeType.Custom_1] == 7,
    "A board card with no saved values keeps what the set-up wrote."
);

Console.WriteLine("ItemEnchantPreview checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class MemorySettingsStore : IBppSettingsStore
{
    private readonly Dictionary<string, object> _bound = new();

    public ISetting<T> Bind<T>(
        string section,
        string key,
        T defaultValue,
        string description,
        SettingRange<T>? acceptableRange = null
    )
        where T : IComparable
    {
        var setting = new Setting<T> { Value = defaultValue };
        _bound[section + "/" + key] = setting;
        return setting;
    }

    public ISetting<bool> Bool(string section, string key) =>
        (ISetting<bool>)_bound[section + "/" + key];

    private sealed class Setting<T> : ISetting<T>
    {
        public T Value { get; set; } = default!;
    }
}
