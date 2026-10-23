#nullable enable
using System.Globalization;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.PvpBattles;

/// <summary>
/// One board capture with the game's card types. Its stored JSON is Core's
/// <see cref="PvpBattleCardSetDocument"/>, the shape every host writes and the Bundle composer
/// reads: <see cref="Serialize"/> and <see cref="Deserialize"/> only map the card enums to and
/// from their member names.
/// </summary>
public sealed class PvpBattleCardSetCapture
{
    public IList<PvpBattleCardSnapshot> Items { get; set; } = new List<PvpBattleCardSnapshot>();

    public PvpBattleCaptureStatus Status { get; set; }

    public PvpBattleCaptureSource Source { get; set; }

    public static string Serialize(PvpBattleCardSetCapture capture)
    {
        if (capture == null)
            throw new ArgumentNullException(nameof(capture));
        return PvpBattleCardSetDocument.Serialize(
            new PvpBattleCardSetDocument
            {
                Items = capture.Items.Select(ToDocument).ToList(),
                Status = capture.Status,
                Source = capture.Source,
            }
        );
    }

    public static PvpBattleCardSetCapture Deserialize(string json)
    {
        var document = PvpBattleCardSetDocument.Deserialize(json);
        return new PvpBattleCardSetCapture
        {
            Items = document.Items.Select(FromDocument).ToList(),
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
            Size = card.Size.ToString(),
            Section = card.Section?.ToString(),
            Socket = card.Socket?.ToString(),
            Name = card.Name,
            Tier = card.Tier,
            Enchant = card.Enchant,
            Tags = new List<string>(card.Tags),
            Attributes = new Dictionary<string, int>(card.Attributes),
        };

    private static PvpBattleCardSnapshot FromDocument(PvpBattleCardDocument card) =>
        new()
        {
            InstanceId = card.InstanceId,
            TemplateId = card.TemplateId,
            Type = Parse<ECardType>(card.Type),
            Size = Parse<ECardSize>(card.Size),
            Section = card.Section != null ? Parse<EInventorySection>(card.Section) : null,
            Socket = card.Socket != null ? Parse<EContainerSocketId>(card.Socket) : null,
            Name = card.Name,
            Tier = card.Tier,
            Enchant = card.Enchant,
            Tags = new List<string>(card.Tags),
            Attributes = new Dictionary<string, int>(card.Attributes),
        };

    // A member name, or the number of a value the game has no name for; a field the JSON leaves out
    // reads as the enum's default. Anything else is a corrupt capture, which fails the read as the
    // serializer's enum converter did.
    private static TEnum Parse<TEnum>(string name)
        where TEnum : struct, Enum =>
        name.Length == 0 ? default
        : Enum.TryParse<TEnum>(name, ignoreCase: false, out var value) ? value
        : throw new InvalidDataException(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} has no member '{1}'.",
                typeof(TEnum).Name,
                name
            )
        );
}
