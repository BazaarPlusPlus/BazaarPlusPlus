using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;

TestBoardSpan_MapsNativeItemSizes();
TestSlotPlanner_ReferencePreservesSourceSockets();
TestSlotPlanner_ReferenceFallsBackWhenSourceMissing();
TestSlotPlanner_SelectableContainerSkipsInvalidSourceSockets();
TestSlotPlanner_SelectableShopCentersByTotalSpan();
TestSlotPlanner_SelectableShopSkipsOverflowRemainder();
TestPreviewMapper_UsesDisplaySocketAndBoardPrefix();
TestPreviewMapper_CarriesDisplaySpan();
TestNativeItemMapperPreservesPlannedCardsAndOwnsAttributes();

Console.WriteLine("HistoryPanelPreview checks passed.");

static void TestBoardSpan_MapsNativeItemSizes()
{
    Assert(BppItemBoardSpan.Resolve(ECardSize.Small) == 1, "Small item should span one slot.");
    Assert(BppItemBoardSpan.Resolve(ECardSize.Medium) == 2, "Medium item should span two slots.");
    Assert(BppItemBoardSpan.Resolve(ECardSize.Large) == 3, "Large item should span three slots.");
    Assert(
        BppItemBoardSpan.Resolve(ECardSize.Small, explicitSpan: 4) == 4,
        "Explicit positive span should override size-derived span."
    );
}

static void TestSlotPlanner_ReferencePreservesSourceSockets()
{
    var board = Board(
        BppItemBoardId.Historical,
        BppItemBoardType.Reference,
        Card(0, ECardSize.Large, source: EContainerSocketId.Socket_4)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board);

    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_4,
        "Reference board should preserve a source socket that can fit the card span."
    );
}

static void TestSlotPlanner_ReferenceFallsBackWhenSourceMissing()
{
    var warnings = new List<string>();
    var board = Board(
        BppItemBoardId.FinalBuild,
        BppItemBoardType.Reference,
        Card(0, ECardSize.Medium),
        Card(1, ECardSize.Large)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board, warnings.Add);

    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_0,
        "First source-less reference card should fall back to slot 0."
    );
    Assert(
        planned.Cards[1].DisplaySocketId == EContainerSocketId.Socket_2,
        "Fallback cursor should advance by the prior card span, not by item count."
    );
    Assert(warnings.Count == 2, "Each source-less reference card should emit a warning.");
}

static void TestSlotPlanner_SelectableContainerSkipsInvalidSourceSockets()
{
    var warnings = new List<string>();
    var board = Board(
        BppItemBoardId.LiveBoard,
        BppItemBoardType.SelectableContainer,
        Card(0, ECardSize.Large, source: EContainerSocketId.Socket_8),
        Card(1, ECardSize.Small, source: EContainerSocketId.Socket_2),
        Card(2, ECardSize.Small)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board, warnings.Add);

    Assert(planned.Cards.Count == 1, "Selectable container should keep only valid source sockets.");
    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_2,
        "Valid source socket should be preserved for selectable containers."
    );
    Assert(warnings.Count == 2, "Overflow and missing sockets should both be warned.");
}

static void TestSlotPlanner_SelectableShopCentersByTotalSpan()
{
    var board = Board(
        BppItemBoardId.LiveShop,
        BppItemBoardType.SelectableShop,
        Card(0, ECardSize.Small),
        Card(1, ECardSize.Medium),
        Card(2, ECardSize.Large)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board);

    Assert(
        planned.Cards[0].DisplaySocketId == EContainerSocketId.Socket_2,
        "Total span 6 should center at slot floor((10 - 6) / 2) = 2."
    );
    Assert(
        planned.Cards[1].DisplaySocketId == EContainerSocketId.Socket_3,
        "Shop cursor should advance by small card span."
    );
    Assert(
        planned.Cards[2].DisplaySocketId == EContainerSocketId.Socket_5,
        "Shop cursor should advance by medium card span."
    );
}

static void TestSlotPlanner_SelectableShopSkipsOverflowRemainder()
{
    var warnings = new List<string>();
    var board = Board(
        BppItemBoardId.LiveShop,
        BppItemBoardType.SelectableShop,
        Card(0, ECardSize.Large),
        Card(1, ECardSize.Large),
        Card(2, ECardSize.Large),
        Card(3, ECardSize.Medium)
    );

    var planned = BppItemBoardSlotPlanner.Plan(board, warnings.Add);

    Assert(planned.Cards.Count == 3, "Overflowing shop item should be skipped.");
    Assert(
        planned.Cards[2].DisplaySocketId == EContainerSocketId.Socket_6,
        "Third large should be the last card that fits on the 10-slot row."
    );
    Assert(warnings.Count == 1, "Overflow should emit one warning.");
}

static void TestPreviewMapper_UsesDisplaySocketAndBoardPrefix()
{
    var board = Board(
        BppItemBoardId.LiveStash,
        BppItemBoardType.SelectableContainer,
        Card(
            0,
            ECardSize.Small,
            source: EContainerSocketId.Socket_1,
            display: EContainerSocketId.Socket_3
        )
    );

    var spec = BppItemBoardPreviewMapper.Map(board).Single();

    Assert(spec.SocketId == EContainerSocketId.Socket_3, "Mapper should prefer display socket.");
    Assert(
        spec.InstanceIdPrefix == "bpp-livestash",
        "Mapper should use the board id as a stable preview instance prefix."
    );
}

static void TestPreviewMapper_CarriesDisplaySpan()
{
    var board = Board(
        BppItemBoardId.LiveBoard,
        BppItemBoardType.SelectableContainer,
        Card(
            0,
            ECardSize.Large,
            source: EContainerSocketId.Socket_1,
            display: EContainerSocketId.Socket_4
        )
    );

    var spec = BppItemBoardPreviewMapper.Map(board).Single();

    Assert(spec.SocketId == EContainerSocketId.Socket_4, "Mapper should preserve display socket.");
    Assert(spec.DisplaySpan == 3, "Mapper should pass the planned display span to the renderer.");
}

static BppItemBoard Board(
    BppItemBoardId id,
    BppItemBoardType type,
    params BppItemBoardCard[] cards
) => new(id, type, cards, $"{id}:{cards.Length}");

static BppItemBoardCard Card(
    int order,
    ECardSize size,
    EContainerSocketId? source = null,
    EContainerSocketId? display = null
) =>
    new()
    {
        TemplateId = Guid.Parse($"00000000-0000-0000-0000-{order + 1:000000000000}"),
        InstanceId = $"instance-{order}",
        Order = order,
        Tier = ETier.Silver,
        Size = size,
        EnchantmentType = EEnchantmentType.Fiery,
        Attributes = new Dictionary<ECardAttributeType, int>
        {
            [ECardAttributeType.BurnApplyAmount] = order + 1,
        },
        SourceSocketId = source,
        DisplaySocketId = display,
    };

static void TestNativeItemMapperPreservesPlannedCardsAndOwnsAttributes()
{
    var source = Card(0, ECardSize.Large, source: EContainerSocketId.Socket_4);
    var board = Board(
        BppItemBoardId.LiveStash,
        BppItemBoardType.SelectableContainer,
        source,
        Card(1, ECardSize.Medium, source: EContainerSocketId.Socket_9)
    );
    var mapped = NativeMonsterBoardItemMapper.Map(board, "preview");
    Assert(mapped.Count == 1, "Native projection must omit cards that exceed the ten sockets.");
    var item = mapped[0];
    Assert(
        item.TemplateId == source.TemplateId,
        "Native projection must retain template identity."
    );
    Assert(item.InstanceId == "preview-0", "Each caller owns its instance prefix.");
    Assert(
        item.SocketId == EContainerSocketId.Socket_4,
        "Container placement must retain source sockets."
    );
    Assert(
        item.Tier == source.Tier && item.EnchantmentType == source.EnchantmentType,
        "Native projection must retain tier and enchantment."
    );
    var attributes = item.Attributes ?? throw new InvalidOperationException("Missing attributes.");
    Assert(
        attributes[ECardAttributeType.BurnApplyAmount] == 1,
        "Native projection must retain attributes."
    );
    attributes[ECardAttributeType.BurnApplyAmount] = 99;
    Assert(
        source.Attributes[ECardAttributeType.BurnApplyAmount] == 1,
        "Native card mutation must not alter source snapshots."
    );

    var shop = NativeMonsterBoardItemMapper.Map(
        Board(BppItemBoardId.LiveShop, BppItemBoardType.SelectableShop, source),
        "shop"
    );
    Assert(
        shop[0].SocketId == EContainerSocketId.Socket_3,
        "Shop projection must use the centered planned socket, not the source socket."
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
