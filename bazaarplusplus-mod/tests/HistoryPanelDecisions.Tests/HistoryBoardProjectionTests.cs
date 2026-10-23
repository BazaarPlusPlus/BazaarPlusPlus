#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.PvpBattles;

// HistoryBoardProjection: a stored battle's hands and skills as the Android boards rent them.
internal static class HistoryBoardProjectionTests
{
    private static readonly Guid Known = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Large = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Loose = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Unknown = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Skill = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static void Run()
    {
        bool HasTemplate(Guid id) => id != Unknown;
        var hand = new PvpBattleCardSetCapture
        {
            Items =
            {
                Card(Loose, CardType.Item, null, null, "Gold", "None"),
                Card(Large, CardType.Item, CardSocket.Socket_4, CardSize.Large, "Diamond", "Fiery"),
                Card(Known, CardType.Item, CardSocket.Socket_1, CardSize.Medium, "Silver", null),
                Card(Unknown, CardType.Item, CardSocket.Socket_0, CardSize.Small, "Gold", null),
                Card(Known, CardType.Skill, CardSocket.Socket_7, CardSize.Small, "Gold", null),
                new PvpBattleCardSnapshot { TemplateId = "not-a-guid", Type = CardType.Item },
                Card(Known, CardType.Item, null, CardSize.Small, "Mythic", "Sparkly"),
            },
        };

        var board = HistoryBoardProjection.Items(hand, HasTemplate, "player:battle");
        Require(board.Signature == "player:battle", "The board keeps its signature.");
        Require(
            board.Cards.Select(card => card.TemplateId).SequenceEqual([Known, Large, Loose, Known]),
            "Placed items come first by socket, then the rest in capture order; skills, unknown "
                + "templates, and unparseable ids drop."
        );
        var medium = board.Cards[0];
        Require(
            medium.SourceSocketId == 1
                && medium.DisplaySocketId == 1
                && medium.DisplaySpan == 2
                && medium.Tier == CardTier.Silver
                && medium.EnchantmentType == null,
            "A placed medium item keeps its socket, span, and tier."
        );
        var large = board.Cards[1];
        Require(
            large.DisplaySocketId == 4
                && large.DisplaySpan == 3
                && large.Tier == CardTier.Diamond
                && large.EnchantmentType == CardEnchantment.Fiery,
            "A large item keeps its socket, tier, and enchantment."
        );
        var loose = board.Cards[2];
        Require(
            loose.Size == CardSize.Small
                && loose.DisplaySpan == 1
                && loose.DisplaySocketId is { } socket
                && socket >= 7
                && loose.EnchantmentType == null
                && loose.Tier == CardTier.Gold,
            "A card with no size or socket renders as one socket after the placed cards."
        );
        var unknownNames = board.Cards[3];
        Require(
            unknownNames.Tier == CardTier.Bronze && unknownNames.EnchantmentType == null,
            "A tier or enchantment the facets do not name falls back to Bronze and none."
        );
        Require(
            board.Cards.All(card => card.DisplaySocketId is not null),
            "Every projected card is planned onto a display socket."
        );

        Require(
            HistoryBoardProjection.Items(null, HasTemplate, "x").Cards.Count == 0,
            "A battle without a capture has an empty board."
        );

        var skills = HistoryBoardProjection.Skills(
            new PvpBattleCardSetCapture
            {
                Items =
                {
                    Card(Skill, CardType.Skill, null, null, "Legendary", null),
                    Card(Known, CardType.Item, null, CardSize.Small, "Gold", null),
                    Card(Unknown, CardType.Skill, null, null, "Gold", null),
                },
            },
            HasTemplate
        );
        Require(
            skills.Count == 1
                && skills[0].TemplateId == Skill
                && skills[0].Tier == CardTier.Legendary,
            "Only known skills project from a skill set."
        );

        Require(
            HistoryBoardProjection.Facts(3, 1, 0).Status == NativeMonsterBoardStatus.Loading,
            "A pending card shows the board loading."
        );
        Require(
            HistoryBoardProjection.Facts(0, 0, 0).Status == NativeMonsterBoardStatus.Empty,
            "A board with no card is empty."
        );
        Require(
            HistoryBoardProjection.Facts(2, 0, 2) is { Status: NativeMonsterBoardStatus.Failed },
            "Every card failed: the board failed."
        );
        Require(
            HistoryBoardProjection.Facts(3, 0, 1)
                is { Status: NativeMonsterBoardStatus.Partial, PartialCount: 1 },
            "Some cards failed: the board is partial with their count."
        );
        Require(
            HistoryBoardProjection.Facts(3, 0, 0).Status == NativeMonsterBoardStatus.Ready,
            "Every card rendered: the board is ready."
        );
    }

    private static PvpBattleCardSnapshot Card(
        Guid templateId,
        CardType type,
        CardSocket? socket,
        CardSize? size,
        string? tier,
        string? enchant
    ) =>
        new()
        {
            InstanceId = $"itm_{templateId:N}"[..12],
            TemplateId = templateId.ToString(),
            Type = type,
            Socket = socket,
            Size = size,
            Tier = tier,
            Enchant = enchant,
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
