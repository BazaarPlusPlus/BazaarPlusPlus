#nullable enable
using System.Globalization;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;

namespace BazaarPlusPlus.Game.BundlePipeline;

/// <summary>
/// The desktop's game-typed reads for the Bundle composer: card enum values from the game's
/// assemblies, and Replay Payload verification through <see cref="CombatReplayLoader"/>.
/// </summary>
internal sealed class DesktopPvpBattleEvidence : IPvpBattleEvidenceBinding
{
    public static DesktopPvpBattleEvidence Instance { get; } = new();

    public int CardEnumValue(PvpCardEnum kind, string name) =>
        kind switch
        {
            PvpCardEnum.CardType => Parse<ECardType>(name),
            PvpCardEnum.CardSize => Parse<ECardSize>(name),
            PvpCardEnum.InventorySection => Parse<EInventorySection>(name),
            PvpCardEnum.ContainerSocket => Parse<EContainerSocketId>(name),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public bool TryCardEnumName(PvpCardEnum kind, int value, out string name)
    {
        name =
            kind switch
            {
                PvpCardEnum.CardType => Name<ECardType>(value),
                PvpCardEnum.CardSize => Name<ECardSize>(value),
                PvpCardEnum.InventorySection => Name<EInventorySection>(value),
                PvpCardEnum.ContainerSocket => Name<EContainerSocketId>(value),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            } ?? string.Empty;
        return name.Length > 0;
    }

    public void VerifyReplay(PvpReplayPayload payload) =>
        _ = new CombatReplayLoader().Load(payload);

    private static string? Name<TEnum>(int value)
        where TEnum : struct, Enum =>
        Enum.GetName(typeof(TEnum), Enum.ToObject(typeof(TEnum), value));

    // A capture stores an enum by name, or by number for a value the game had no name for.
    private static int Parse<TEnum>(string name)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(name, ignoreCase: false, out var value)
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : throw new InvalidDataException($"{typeof(TEnum).Name} has no member '{name}'.");
}
