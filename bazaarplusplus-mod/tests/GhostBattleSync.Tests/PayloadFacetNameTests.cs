#nullable enable
using System.IO.Compression;
using System.Text;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.TestSupport;

// Ghost and favorite payload files carry a manifest whose cards are BPP facets. A facet's integer
// is BPP's own and never persisted (ADR-0015 §4), so both files store every enum by member name
// and read back to the same facets.
internal static class PayloadFacetNameTests
{
    private static readonly string[] MemberNames =
    [
        "SocketEffect",
        "Medium",
        "Stash",
        "Socket_3",
        "CapturedEmpty",
        "LiveRetry",
    ];

    internal static void Run()
    {
        var ghostBytes = GhostBattlePayloadCodec.Serialize(
            new GhostBattlePayload
            {
                BattleId = "ghost-battle",
                PerspectiveVersion = 1,
                BattleManifest = Manifest("ghost-battle"),
                ReplayPayload = new PvpReplayPayload { BattleId = "ghost-battle" },
            }
        );
        RequireNames(ghostBytes, "A ghost payload");
        Require(
            GhostBattlePayloadCodec.TryDeserialize(ghostBytes, out var ghost, out _, out _)
                && Card(ghost!.BattleManifest) is var card
                && card.Type == CardType.SocketEffect
                && card.Size == CardSize.Medium
                && card.Section == CardSection.Stash
                && card.Socket == CardSocket.Socket_3
                && ghost.BattleManifest.Snapshots.PlayerSkills.Items.Single().Size == null
                && ghost.BattleManifest.Snapshots.OpponentHand.Status
                    == PvpBattleCaptureStatus.CapturedEmpty,
            "A ghost payload must read back to the same facets."
        );
        Require(
            !GhostBattlePayloadCodec.TryDeserialize(
                MessagePackGzipCodec.Serialize(
                    new GhostBattlePayload { BattleManifest = Manifest("ghost-battle") }
                ),
                out _,
                out _,
                out _
            ),
            "A ghost payload whose enums are integers must not decode."
        );

        var root = Path.Combine(Path.GetTempPath(), $"bpp-favorite-names-{Guid.NewGuid():N}");
        try
        {
            var store = new FavoriteBattlePayloadStore(root);
            store.Save(
                new FavoriteBattlePayload
                {
                    BattleId = "favorite-battle",
                    BattleManifest = Manifest("favorite-battle"),
                    ReplayPayload = new PvpReplayPayload { BattleId = "favorite-battle" },
                }
            );
            RequireNames(
                TestInputs.ScratchBytes(Path.Combine(root, "favorite-battle.favorite.mpack.gz")),
                "A favorite payload"
            );
            var loaded = store.LoadDetailed("favorite-battle");
            Require(
                loaded.Status == FileBackedPayloadLoadStatus.Loaded
                    && Card(loaded.Payload!.BattleManifest).Socket == CardSocket.Socket_3,
                "A favorite payload must read back to the same facets."
            );
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static PvpBattleManifest Manifest(string battleId) =>
        new()
        {
            BattleId = battleId,
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = new PvpBattleCardSetCapture
                {
                    Status = PvpBattleCaptureStatus.Captured,
                    Source = PvpBattleCaptureSource.LiveRetry,
                    Items =
                    [
                        new PvpBattleCardSnapshot
                        {
                            InstanceId = "card",
                            Type = CardType.SocketEffect,
                            Size = CardSize.Medium,
                            Section = CardSection.Stash,
                            Socket = CardSocket.Socket_3,
                        },
                    ],
                },
                PlayerSkills = new PvpBattleCardSetCapture
                {
                    Status = PvpBattleCaptureStatus.Captured,
                    Items =
                    [
                        new PvpBattleCardSnapshot { InstanceId = "skill", Type = CardType.Skill },
                    ],
                },
                OpponentHand = new PvpBattleCardSetCapture
                {
                    Status = PvpBattleCaptureStatus.CapturedEmpty,
                },
            },
        };

    private static PvpBattleCardSnapshot Card(PvpBattleManifest manifest) =>
        manifest.Snapshots.PlayerHand.Items.Single();

    private static void RequireNames(byte[] gzip, string what)
    {
        using var input = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        var text = Encoding.UTF8.GetString(output.ToArray());
        foreach (var name in MemberNames)
            Require(text.Contains(name, StringComparison.Ordinal), $"{what} must store {name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
