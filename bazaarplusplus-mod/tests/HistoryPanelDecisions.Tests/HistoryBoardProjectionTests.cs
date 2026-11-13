#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Localization;
using Xunit;

// HistoryBoardProjection: a stored battle's hands and skills as the Android boards rent them.
public sealed class HistoryBoardProjectionTests
{
    // The capsule ran these checks with English installed.
    public HistoryBoardProjectionTests() =>
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);

    private static readonly Guid Known = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Large = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Loose = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Unknown = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Skill = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void ProjectsStoredBattleBoards()
    {
        bool HasTemplate(Guid id) => id != Unknown;
        string? NoSocketEffect(Guid id, CardTier tier) => null;
        var hand = new PvpBattleCardSetCapture
        {
            Items =
            {
                Card(Loose, CardType.Item, null, null, "Gold", "None"),
                Card(
                    Large,
                    CardType.Item,
                    CardSocket.Socket_4,
                    CardSize.Large,
                    "Diamond",
                    "Fiery",
                    new Dictionary<string, int> { ["DamageAmount"] = 40, ["PoisonApplyAmount"] = 6 }
                ),
                Card(Known, CardType.Item, CardSocket.Socket_1, CardSize.Medium, "Silver", null),
                Card(Unknown, CardType.Item, CardSocket.Socket_0, CardSize.Small, "Gold", null),
                Card(Known, CardType.Skill, CardSocket.Socket_7, CardSize.Small, "Gold", null),
                new PvpBattleCardSnapshot { TemplateId = "not-a-guid", Type = CardType.Item },
                Card(Known, CardType.Item, null, CardSize.Small, "Mythic", "Sparkly"),
            },
        };

        var board = HistoryBoardProjection.Items(
            hand,
            HasTemplate,
            NoSocketEffect,
            "player:battle"
        );
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
                && large.EnchantmentType == CardEnchantment.Fiery
                && large.Attributes.Count == 2
                && large.Attributes["DamageAmount"] == 40
                && large.Attributes["PoisonApplyAmount"] == 6,
            "A large item keeps its socket, tier, enchantment, and saved values."
        );
        Require(medium.Attributes.Count == 0, "A card saved without values carries none.");
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
            HistoryBoardProjection.Items(null, HasTemplate, NoSocketEffect, "x").Cards.Count == 0,
            "A battle without a capture has an empty board."
        );

        var skills = HistoryBoardProjection.Skills(
            new PvpBattleCardSetCapture
            {
                Items =
                {
                    Card(
                        Skill,
                        CardType.Skill,
                        null,
                        null,
                        "Legendary",
                        null,
                        new Dictionary<string, int> { ["DamageAmount"] = 55 }
                    ),
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
            skills[0].Attributes.Count == 1 && skills[0].Attributes["DamageAmount"] == 55,
            "A skill keeps its saved values."
        );
        Require(
            HistoryBoardProjection
                .Skills(
                    new PvpBattleCardSetCapture
                    {
                        Items =
                        {
                            new PvpBattleCardSnapshot
                            {
                                TemplateId = Skill.ToString(),
                                Type = CardType.Skill,
                                Attributes = null!,
                            },
                        },
                    },
                    HasTemplate
                )[0]
                .Attributes.Count == 0,
            "A skill saved without a value bag carries none."
        );

        SocketEffects(HasTemplate);

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

    // A socket effect raises its attribute to at least 1 on the placed items that cover its
    // socket, and is no card on the board.
    private static void SocketEffects(Func<Guid, bool> hasTemplate)
    {
        var heat = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var loose = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var lookups = new List<CardTier>();
        string? Lookup(Guid id, CardTier tier)
        {
            lookups.Add(tier);
            return id == heat ? "Heated" : null;
        }
        var hand = new PvpBattleCardSetCapture
        {
            Items =
            {
                Card(heat, CardType.SocketEffect, CardSocket.Socket_2, null, "Silver", null),
                Card(loose, CardType.SocketEffect, CardSocket.Socket_6, null, "Gold", null),
                new PvpBattleCardSnapshot
                {
                    TemplateId = heat.ToString(),
                    Type = CardType.SocketEffect,
                    Attributes = new Dictionary<string, int>(),
                },
                Card(Large, CardType.Item, CardSocket.Socket_1, CardSize.Large, "Gold", null),
                Card(
                    Known,
                    CardType.Item,
                    CardSocket.Socket_5,
                    CardSize.Medium,
                    "Gold",
                    null,
                    new Dictionary<string, int> { ["DamageAmount"] = 9 }
                ),
            },
        };
        var board = HistoryBoardProjection.Items(hand, hasTemplate, Lookup, "socket");
        Require(
            board.Cards.Select(card => card.TemplateId).SequenceEqual([Large, Known]),
            "A socket effect is no card on the board."
        );
        Require(
            board.Cards[0].Attributes.Count == 1 && board.Cards[0].Attributes["Heated"] == 1,
            "A large item at socket 1 covers socket 2, so its effect's attribute is raised to 1."
        );
        Require(
            board.Cards[1].Attributes.Count == 1 && board.Cards[1].Attributes["DamageAmount"] == 9,
            "An item that does not cover the effect's socket, or one whose effect names no "
                + "attribute, keeps its saved values."
        );
        Require(
            lookups.SequenceEqual([CardTier.Silver, CardTier.Gold]),
            "Each placed socket effect is looked up at its own tier; one with no socket is not."
        );

        foreach (var (saved, shown) in new[] { (3, 3), (0, 1) })
        {
            var raised = HistoryBoardProjection.Items(
                new PvpBattleCardSetCapture
                {
                    Items =
                    {
                        Card(heat, CardType.SocketEffect, CardSocket.Socket_0, null, null, null),
                        Card(
                            Known,
                            CardType.Item,
                            CardSocket.Socket_0,
                            CardSize.Small,
                            "Gold",
                            null,
                            new Dictionary<string, int> { ["Heated"] = saved }
                        ),
                    },
                },
                hasTemplate,
                Lookup,
                "raised"
            );
            Require(
                raised.Cards.Single().Attributes["Heated"] == shown,
                "A saved value above 0 stays; 0 is raised to 1."
            );
        }
    }

    private static PvpBattleCardSnapshot Card(
        Guid templateId,
        CardType type,
        CardSocket? socket,
        CardSize? size,
        string? tier,
        string? enchant,
        Dictionary<string, int>? attributes = null
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
            Attributes = attributes ?? new Dictionary<string, int>(),
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
            Xunit.Assert.Fail(message);
    }
}
