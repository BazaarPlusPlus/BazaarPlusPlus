using System.Text.Json;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.TestSupport;

// The desktop's Bundle evidence over the game's Mono assemblies, held to the card enum table that
// AndroidHost.Tests holds the Android host's interop enums to, so both hosts write the same numbers.
internal static class CardEnumTableTests
{
    private const string CardEnumFixture = "tests/Shared/fixtures/pvp-card-enums.json";

    internal static void Run()
    {
        using var table = JsonDocument.Parse(TestInputs.Fixture(CardEnumFixture));
        Verify(table, PvpCardEnum.CardType, typeof(ECardType));
        Verify(table, PvpCardEnum.CardSize, typeof(ECardSize));
        Verify(table, PvpCardEnum.InventorySection, typeof(EInventorySection));
        Verify(table, PvpCardEnum.ContainerSocket, typeof(EContainerSocketId));
    }

    private static void Verify(JsonDocument table, PvpCardEnum kind, Type gameEnum)
    {
        var expected = table
            .RootElement.GetProperty(kind.ToString())
            .EnumerateObject()
            .ToDictionary(member => member.Name, member => member.Value.GetInt32());
        Check.That(
            expected
                .Keys.Order(StringComparer.Ordinal)
                .SequenceEqual(Enum.GetNames(gameEnum).Order(StringComparer.Ordinal)),
            $"{gameEnum.Name} members match {CardEnumFixture}."
        );
        foreach (var (name, value) in expected)
        {
            Check.That(
                DesktopPvpBattleEvidence.Instance.CardEnumValue(kind, name) == value,
                $"{gameEnum.Name}.{name} is {value} on the desktop."
            );
            Check.That(
                DesktopPvpBattleEvidence.Instance.TryCardEnumName(kind, value, out var read)
                    && read == name,
                $"{gameEnum.Name} {value} is named {name} on the desktop."
            );
        }
        Check.That(
            !DesktopPvpBattleEvidence.Instance.TryCardEnumName(PvpCardEnum.CardSize, 0, out _),
            "The game's size 0 has no name on the desktop."
        );
    }
}
