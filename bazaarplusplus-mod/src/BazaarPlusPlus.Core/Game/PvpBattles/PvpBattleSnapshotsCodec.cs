#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// The one mapping between facet board captures (<see cref="PvpBattleCardSetCapture"/>) and the
/// stored <see cref="PvpBattleCardSetDocument"/> JSON every host writes and the Bundle composer
/// reads. A facet is stored by its member name, which equals the game's (ADR-0015 §4).
/// </summary>
/// <remarks>
/// A card the game gave no size is stored with <see cref="PvpBattleCardDocument.NoSize"/>, the
/// game's unnamed size 0, and reads back with a null <see cref="PvpBattleCardSnapshot.Size"/>.
/// A stored type or size that names no facet drops that card from the facet view (a warning names
/// the kind); a section or socket that names no facet reads as null.
/// </remarks>
public static class PvpBattleSnapshotsCodec
{
    public static string Serialize(PvpBattleCardSetCapture capture) =>
        PvpBattleCardSetDocument.Serialize(ToDocument(capture));

    public static PvpBattleCardSetCapture Deserialize(string json) =>
        FromDocument(PvpBattleCardSetDocument.Deserialize(json));

    public static PvpBattleSnapshotDocuments ToDocuments(PvpBattleSnapshots snapshots)
    {
        if (snapshots == null)
            throw new ArgumentNullException(nameof(snapshots));
        return new PvpBattleSnapshotDocuments
        {
            PlayerHand = Serialize(snapshots.PlayerHand),
            PlayerSkills = Serialize(snapshots.PlayerSkills),
            OpponentHand = Serialize(snapshots.OpponentHand),
            OpponentSkills = Serialize(snapshots.OpponentSkills),
        };
    }

    // An empty document (a column the row left NULL) reads as an empty capture.
    public static PvpBattleSnapshots FromDocuments(PvpBattleSnapshotDocuments documents)
    {
        if (documents == null)
            throw new ArgumentNullException(nameof(documents));
        return new PvpBattleSnapshots
        {
            PlayerHand = Deserialize(documents.PlayerHand),
            PlayerSkills = Deserialize(documents.PlayerSkills),
            OpponentHand = Deserialize(documents.OpponentHand),
            OpponentSkills = Deserialize(documents.OpponentSkills),
        };
    }

    public static PvpBattleCardSetDocument ToDocument(PvpBattleCardSetCapture capture)
    {
        if (capture == null)
            throw new ArgumentNullException(nameof(capture));
        return new PvpBattleCardSetDocument
        {
            Items = capture.Items.Select(ToDocument).ToList(),
            Status = capture.Status,
            Source = capture.Source,
        };
    }

    public static PvpBattleCardSetCapture FromDocument(PvpBattleCardSetDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));
        var items = new List<PvpBattleCardSnapshot>(document.Items.Count);
        foreach (var card in document.Items)
        {
            if (TryFromDocument(card, out var snapshot))
                items.Add(snapshot);
        }
        return new PvpBattleCardSetCapture
        {
            Items = items,
            Status = document.Status,
            Source = document.Source,
        };
    }

    private static PvpBattleCardDocument ToDocument(PvpBattleCardSnapshot card) =>
        new()
        {
            InstanceId = card.InstanceId,
            TemplateId = card.TemplateId,
            Type = card.Type.ToString(),
            Size = card.Size?.ToString() ?? PvpBattleCardDocument.NoSize,
            Section = card.Section?.ToString(),
            Socket = card.Socket?.ToString(),
            Name = card.Name,
            Tier = card.Tier,
            Enchant = card.Enchant,
            Tags = new List<string>(card.Tags),
            Attributes = new Dictionary<string, int>(card.Attributes),
        };

    private static bool TryFromDocument(
        PvpBattleCardDocument card,
        out PvpBattleCardSnapshot snapshot
    )
    {
        snapshot = null!;
        if (!TryParseFacet<CardType>(card.Type, out var type))
        {
            LogDropped(PvpCardEnum.CardType, card.Type);
            return false;
        }
        CardSize? size = null;
        if (card.Size.Length > 0 && card.Size != PvpBattleCardDocument.NoSize)
        {
            if (!TryParseFacet<CardSize>(card.Size, out var parsedSize))
            {
                LogDropped(PvpCardEnum.CardSize, card.Size);
                return false;
            }
            size = parsedSize;
        }
        snapshot = new PvpBattleCardSnapshot
        {
            InstanceId = card.InstanceId,
            TemplateId = card.TemplateId,
            Type = type,
            Size = size,
            Section = TryParseFacet<CardSection>(card.Section, out var section) ? section : null,
            Socket = TryParseFacet<CardSocket>(card.Socket, out var socket) ? socket : null,
            Name = card.Name,
            Tier = card.Tier,
            Enchant = card.Enchant,
            Tags = new List<string>(card.Tags),
            Attributes = new Dictionary<string, int>(card.Attributes),
        };
        return true;
    }

    /// <summary>
    /// Parses an exact facet member name. A number is never a facet name, even one
    /// <c>Enum.TryParse</c> would cast (ADR-0015 §4).
    /// </summary>
    public static bool TryParseFacet<TFacet>(string? name, out TFacet facet)
        where TFacet : struct, Enum
    {
        facet = default;
        if (string.IsNullOrEmpty(name) || !Enum.IsDefined(typeof(TFacet), name!))
            return false;
        facet = (TFacet)Enum.Parse(typeof(TFacet), name!);
        return true;
    }

    private static void LogDropped(PvpCardEnum kind, string value) =>
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.PvpBattles,
                "pvp_battles.snapshot.card_dropped",
                storm: ["kind"]
            ),
            ("kind", kind),
            ("value", value)
        );
}
