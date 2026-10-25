using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.PvpBattles;
using Il2CppBazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android host's game-typed reads for the Bundle composer, which runs on the seal worker
/// thread and so never calls into the IL2CPP runtime: card enum values come from the interop
/// enums (managed copies of the game's, same names and values), and a Replay Payload is checked
/// for well-formed MessagePack. Only playback decodes a message with the game's serializer
/// (<see cref="AndroidReplayMessages.Deserialize{T}"/>).
/// </summary>
internal sealed class AndroidPvpBattleEvidence : IPvpBattleEvidenceBinding
{
    public static AndroidPvpBattleEvidence Instance { get; } = new();

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

    private static string? Name<TEnum>(int value)
        where TEnum : struct, Enum =>
        Enum.GetName(typeof(TEnum), Enum.ToObject(typeof(TEnum), value));

    public void VerifyReplay(PvpReplayPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        AndroidReplayMessages.VerifyWellFormed(payload.SpawnMessageBytes, "spawn");
        AndroidReplayMessages.VerifyWellFormed(payload.CombatMessageBytes, "combat");
        AndroidReplayMessages.VerifyWellFormed(payload.DespawnMessageBytes, "despawn");
    }

    // A capture stores an enum by name, or by number for a value the game had no name for.
    private static int Parse<TEnum>(string name)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(name, ignoreCase: false, out var value)
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : throw new InvalidDataException($"{typeof(TEnum).Name} has no member '{name}'.");
}
