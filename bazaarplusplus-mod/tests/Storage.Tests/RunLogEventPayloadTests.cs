#nullable enable
using BazaarPlusPlus.Storage;
using BazaarPlusPlus.Storage.RunLog;
using Newtonsoft.Json;

internal static class RunLogEventPayloadTests
{
    // Properties that were never written by any construction site and were removed.
    // NullValueHandling.Ignore already kept them out of payload_json; this pins that.
    private static readonly string[] NeverSerializedNames =
    [
        "current_hour_xp",
        "parent_encounter_id",
        "reroll_cost",
        "rerolls_remaining",
        "state_fingerprint",
        "abandoned_reason",
        "inferred_from",
        "confidence",
    ];

    internal static void Run()
    {
        RunStartedPayloadIsUnchanged();
        PvpCombatRecordedPayloadIsUnchanged();
    }

    private static void RunStartedPayloadIsUnchanged()
    {
        // Shaped exactly as RunLoggingModule builds a run_started event, plus the
        // RunId/Seq/Ts that RunLogStore stamps before serializing.
        var payload = Serialize(
            new RunLogEvent
            {
                RunId = "run-1",
                Seq = 7,
                Ts = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero),
                Kind = "run_started",
                Day = 3,
                Hour = 2,
                Hero = "Vanessa",
                GameMode = "Ranked",
            }
        );

        Equal(
            "{\"schema_version\":2,\"run_id\":\"run-1\",\"seq\":7,"
                + "\"ts\":\"2026-01-02T03:04:05.678+00:00\",\"kind\":\"run_started\","
                + "\"day\":3,\"hour\":2,\"hero\":\"Vanessa\",\"game_mode\":\"Ranked\"}",
            payload,
            "run_started payload_json"
        );
        AssertNoRemovedNames(payload, "run_started");
    }

    private static void PvpCombatRecordedPayloadIsUnchanged()
    {
        var payload = Serialize(
            new RunLogEvent
            {
                RunId = "run-1",
                Seq = 8,
                Ts = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero),
                Kind = "pvp_combat_recorded",
                Day = 3,
                Hour = 2,
                EncounterId = "encounter-9",
                CombatKind = "PVPCombat",
                BattleId = "battle-4",
                OpponentName = "Rival",
            }
        );

        Equal(
            "{\"schema_version\":2,\"run_id\":\"run-1\",\"seq\":8,"
                + "\"ts\":\"2026-01-02T03:04:05.678+00:00\",\"kind\":\"pvp_combat_recorded\","
                + "\"day\":3,\"hour\":2,\"encounter_id\":\"encounter-9\","
                + "\"combat_kind\":\"PVPCombat\",\"battle_id\":\"battle-4\","
                + "\"opponent_name\":\"Rival\"}",
            payload,
            "pvp_combat_recorded payload_json"
        );
        AssertNoRemovedNames(payload, "pvp_combat_recorded");
    }

    private static void AssertNoRemovedNames(string payload, string label)
    {
        foreach (var name in NeverSerializedNames)
        {
            if (payload.Contains(name, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{label} payload_json unexpectedly contains '{name}': {payload}"
                );
        }
    }

    private static string Serialize(RunLogEvent entry) =>
        JsonConvert.SerializeObject(
            entry,
            SerializerSettingsFactory.CreateSerializerSettings(includeStringEnumConverter: false)
        );

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Expected {label} to be '{expected}', got '{actual}'."
            );
    }
}
