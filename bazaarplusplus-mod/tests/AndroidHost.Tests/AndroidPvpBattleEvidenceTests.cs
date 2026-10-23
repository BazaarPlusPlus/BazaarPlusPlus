using System.Text.Json;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.TestSupport;
using Il2CppBazaarGameShared.Domain.Core.Types;
using MessagePack;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The compiled host's Bundle evidence, run against the interop enums the host ships with. The
/// card enum fixture is shared with BundlePipeline.Tests, which holds the desktop's evidence over
/// the game's Mono assemblies to the same table, so both hosts write the same numbers.
/// </summary>
public sealed class AndroidPvpBattleEvidenceTests
{
    private const string CardEnumFixture = "tests/Shared/fixtures/pvp-card-enums.json";

    public static TheoryData<PvpCardEnum, Type> CardEnums() =>
        new()
        {
            { PvpCardEnum.CardType, typeof(ECardType) },
            { PvpCardEnum.CardSize, typeof(ECardSize) },
            { PvpCardEnum.InventorySection, typeof(EInventorySection) },
            { PvpCardEnum.ContainerSocket, typeof(EContainerSocketId) },
        };

    [Theory]
    [MemberData(nameof(CardEnums))]
    public void Card_enum_values_are_the_interop_enums_and_match_the_shared_table(
        PvpCardEnum kind,
        Type interopEnum
    )
    {
        var expected = SharedTable(kind);

        Assert.Equal(
            expected.Keys.Order(StringComparer.Ordinal),
            Enum.GetNames(interopEnum).Order(StringComparer.Ordinal)
        );
        foreach (var (name, value) in expected)
        {
            Assert.Equal(value, Convert.ToInt32(Enum.Parse(interopEnum, name)));
            Assert.Equal(value, AndroidPvpBattleEvidence.Instance.CardEnumValue(kind, name));
        }
    }

    [Fact]
    public void A_value_the_game_has_no_name_for_is_read_as_its_number()
    {
        Assert.Equal(
            99,
            AndroidPvpBattleEvidence.Instance.CardEnumValue(PvpCardEnum.CardType, "99")
        );
    }

    [Theory]
    [InlineData("Weapon")]
    [InlineData("skill")]
    [InlineData("")]
    public void An_unknown_name_fails_the_read(string name)
    {
        Assert.Throws<InvalidDataException>(() =>
            AndroidPvpBattleEvidence.Instance.CardEnumValue(PvpCardEnum.CardType, name)
        );
    }

    [Fact]
    public void A_payload_of_three_well_formed_messages_verifies()
    {
        AndroidPvpBattleEvidence.Instance.VerifyReplay(ReplayMessages.Payload("battle"));
    }

    [Fact]
    public void An_empty_message_fails_verification_naming_its_part()
    {
        var payload = ReplayMessages.Payload("battle");
        payload.CombatMessageBytes = [];

        var failure = Assert.Throws<InvalidDataException>(() =>
            AndroidPvpBattleEvidence.Instance.VerifyReplay(payload)
        );
        Assert.Contains("'combat'", failure.Message);
    }

    [Fact]
    public void Trailing_bytes_after_one_message_fail_verification()
    {
        var bytes = ReplayMessages.Message("despawn").Concat(new byte[] { 0xC0 }).ToArray();

        var failure = Assert.Throws<InvalidDataException>(() =>
            AndroidReplayMessages.VerifyWellFormed(bytes, "despawn")
        );
        Assert.Contains("trailing", failure.Message);
    }

    [Fact]
    public void A_truncated_message_fails_verification()
    {
        var bytes = ReplayMessages.Message("spawn");

        Assert.ThrowsAny<Exception>(() =>
            AndroidReplayMessages.VerifyWellFormed(bytes[..^3], "spawn")
        );
    }

    [Fact]
    public void A_null_payload_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AndroidPvpBattleEvidence.Instance.VerifyReplay(null!)
        );
    }

    private static Dictionary<string, int> SharedTable(PvpCardEnum kind)
    {
        using var table = JsonDocument.Parse(TestInputs.Fixture(CardEnumFixture));
        return table
            .RootElement.GetProperty(kind.ToString())
            .EnumerateObject()
            .ToDictionary(member => member.Name, member => member.Value.GetInt32());
    }
}

/// <summary>MessagePack values shaped like game messages: an array holding a message id.</summary>
internal static class ReplayMessages
{
    public static byte[] Message(string part) =>
        MessagePackSerializer.Serialize<object[]>([part + "-message-id", 7, new[] { 1, 2, 3 }]);

    public static PvpReplayPayload Payload(string battleId) =>
        new()
        {
            BattleId = battleId,
            Version = 1,
            SpawnMessageBytes = Message("spawn"),
            CombatMessageBytes = Message("combat"),
            DespawnMessageBytes = Message("despawn"),
        };
}
