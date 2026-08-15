#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal sealed class RunPayloadComposer
{
    private static readonly Regex Identifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant
    );
    private static readonly JsonSerializerSettings SnapshotJson =
        SerializerSettingsFactory.CreateSerializerSettings(includeStringEnumConverter: true);
    private readonly string _databasePath;
    private readonly PvpBattleCatalog _battleCatalog;
    private readonly CombatReplayPayloadStore _replayStore;

    internal RunPayloadComposer(string databasePath, string replayRoot)
    {
        _databasePath = databasePath;
        _battleCatalog = new PvpBattleCatalog(databasePath);
        _replayStore = new CombatReplayPayloadStore(replayRoot);
    }

    internal string ResolvePlayerAccountId(string runId, string? frozenAccountId)
    {
        var frozen = NormalizeIdentifier(frozenAccountId);
        if (frozen != null)
            return frozen;
        var candidates = _battleCatalog
            .ListByRunId(runId)
            .Select(battle => NormalizeIdentifier(battle.Participants.PlayerAccountId))
            .Where(value => value != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return candidates.Count switch
        {
            1 => candidates[0]!,
            0 => throw new BundleCompositionException("player_account_id_missing"),
            _ => throw new BundleCompositionException("player_account_id_conflict"),
        };
    }

    internal RunPayloadComposition Compose(string runId, string playerAccountId)
    {
        var payload = new RunPayloadV5
        {
            RunId = runId,
            PlayerAccountId = playerAccountId,
            Run = ReadRunFacts(runId),
            Events = ReadEvents(runId),
        };
        var manifests = _battleCatalog
            .ListByRunId(runId)
            .OrderBy(battle => battle.RecordedAtUtc)
            .ThenBy(battle => battle.BattleId, StringComparer.Ordinal)
            .ToList();
        var finalBattleId =
            payload.Run.Status == "completed" ? manifests.LastOrDefault()?.BattleId : null;
        foreach (var manifest in manifests)
        {
            var battle = MapBattle(manifest, finalBattleId);
            payload.Battles.Add(battle);
            if (RunBundleV5Contract.IsReplayable(battle))
                payload.ReplayableBattleIds.Add(battle.BattleId);
            else
                payload.Degradation.ReplayOmittedBattleIds.Add(battle.BattleId);
        }

        var projections = BuildProjections(payload, playerAccountId);
        ApplySizeBudget(payload, projections);
        var encoded = RunPayloadV5Codec.Encode(payload);
        if (encoded.Length > BundleLimitsV5.MaxRunBytes)
            throw new BundleCompositionException("minimal_run_payload_too_large");
        return new RunPayloadComposition(payload, projections, encoded);
    }

    private RunFactsV5 ReadRunFacts(string runId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT hero, game_mode, seed, started_at_utc, ended_at_utc, status,
                   COALESCE(final_day, day), COALESCE(final_hour, hour), victories, losses,
                   player_rank, player_rating, final_player_rank, final_player_rating,
                   final_player_rating_delta, max_health, prestige, level, income, gold,
                   build_channel, mod_version
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId AND completed = 1
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new BundleCompositionException("source_run_missing");
        return new RunFactsV5
        {
            Hero = reader.GetString(0),
            GameMode = reader.GetString(1),
            Seed = NullableInt(reader, 2),
            StartedAtUtc = reader.GetString(3),
            EndedAtUtc = NullableString(reader, 4),
            Status = reader.GetString(5),
            Day = NonNegative(NullableInt(reader, 6)),
            Hour = NonNegative(NullableInt(reader, 7)),
            Victories = NonNegative(NullableInt(reader, 8)),
            Losses = NonNegative(NullableInt(reader, 9)),
            PlayerRank = Limit(NullableString(reader, 10)),
            PlayerRating = NonNegative(NullableInt(reader, 11)),
            FinalPlayerRank = Limit(NullableString(reader, 12)),
            FinalPlayerRating = NonNegative(NullableInt(reader, 13)),
            FinalPlayerRatingDelta = NullableInt(reader, 14),
            MaxHealth = NonNegative(NullableInt(reader, 15)),
            Prestige = NonNegative(NullableInt(reader, 16)),
            Level = NonNegative(NullableInt(reader, 17)),
            Income = NonNegative(NullableInt(reader, 18)),
            Gold = NonNegative(NullableInt(reader, 19)),
            BuildChannel = Limit(NullableString(reader, 20)),
            ModVersion = Limit(NullableString(reader, 21)) ?? string.Empty,
        };
    }

    private List<RunEventV5> ReadEvents(string runId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT seq, ts_utc, kind, payload_json
            FROM {RunLogSchema.RunEventsTableName}
            WHERE run_id = $runId
            ORDER BY seq ASC;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        var events = new List<RunEventV5>();
        while (reader.Read())
            events.Add(
                new RunEventV5
                {
                    Seq = reader.GetInt64(0),
                    TimestampUtc = reader.GetString(1),
                    Kind = reader.GetString(2),
                    PayloadJson = reader.GetString(3),
                }
            );
        return events;
    }

    private RunBattleV5 MapBattle(PvpBattleManifest manifest, string? finalBattleId)
    {
        BattleReplayV5? replay = null;
        var loaded = _replayStore.LoadDetailed(manifest.BattleId);
        if (loaded.Status == FileBackedPayloadLoadStatus.Loaded && loaded.Payload != null)
        {
            try
            {
                _ = new CombatReplayLoader().Load(loaded.Payload);
                replay = new BattleReplayV5
                {
                    Version = loaded.Payload.Version,
                    SpawnMessageBytes = loaded.Payload.SpawnMessageBytes.ToArray(),
                    CombatMessageBytes = loaded.Payload.CombatMessageBytes.ToArray(),
                    DespawnMessageBytes = loaded.Payload.DespawnMessageBytes.ToArray(),
                };
            }
            catch
            {
                replay = null;
            }
        }

        return new RunBattleV5
        {
            BattleId = manifest.BattleId,
            Facts = new BattleFactsV5
            {
                RecordedAtUtc = manifest.RecordedAtUtc.ToUniversalTime().ToString("o"),
                Day = NonNegative(manifest.Day),
                Hour = NonNegative(manifest.Hour),
                EncounterId = Limit(manifest.EncounterId),
                CombatKind = NormalizeIdentifier(manifest.CombatKind) ?? "pvp",
                Result = NormalizeResult(manifest.Outcome.Result),
                WinnerCombatantId = Limit(manifest.Outcome.WinnerCombatantId),
                LoserCombatantId = Limit(manifest.Outcome.LoserCombatantId),
                IsFinalBattle = string.Equals(
                    manifest.BattleId,
                    finalBattleId,
                    StringComparison.Ordinal
                ),
            },
            Participants = new BattleParticipantsV5
            {
                Player = MapParticipant(manifest.Participants, player: true),
                Opponent = MapParticipant(manifest.Participants, player: false),
            },
            Snapshots = new BattleCardSnapshotsV5
            {
                CardSets =
                [
                    MapCardSet("player_hand", manifest.Snapshots.PlayerHand),
                    MapCardSet("player_skills", manifest.Snapshots.PlayerSkills),
                    MapCardSet("opponent_hand", manifest.Snapshots.OpponentHand),
                    MapCardSet("opponent_skills", manifest.Snapshots.OpponentSkills),
                ],
            },
            Replay = replay,
        };
    }

    private static BattleParticipantV5 MapParticipant(
        PvpBattleParticipants participants,
        bool player
    ) =>
        new()
        {
            AccountId = player ? participants.PlayerAccountId : participants.OpponentAccountId,
            DisplayName = Limit(player ? participants.PlayerName : participants.OpponentName),
            HeroName = Limit(player ? participants.PlayerHero : participants.OpponentHero),
            Rank = Limit(player ? participants.PlayerRank : participants.OpponentRank),
            Rating = NonNegative(player ? participants.PlayerRating : participants.OpponentRating),
            Level = NonNegative(player ? participants.PlayerLevel : participants.OpponentLevel),
            Prestige = NonNegative(
                player ? participants.PlayerPrestige : participants.OpponentPrestige
            ),
            Victories = NonNegative(
                player ? participants.PlayerVictories : participants.OpponentVictories
            ),
            Income = NonNegative(player ? participants.PlayerIncome : null),
            Gold = NonNegative(player ? participants.PlayerGold : null),
        };

    private static BattleCardSetV5 MapCardSet(string label, PvpBattleCardSetCapture source) =>
        new()
        {
            Label = label,
            Status = source.Status.ToString(),
            Source = source.Source.ToString(),
            Items = source.Items.Select(MapCard).ToList(),
        };

    private static BattleCardV5 MapCard(PvpBattleCardSnapshot source) =>
        new()
        {
            InstanceId = source.InstanceId,
            TemplateId = source.TemplateId,
            Type = (int)source.Type,
            Size = (int)source.Size,
            Section = source.Section.HasValue ? (int)source.Section.Value : null,
            Socket = source.Socket.HasValue ? (int)source.Socket.Value : null,
            Name = Limit(source.Name),
            Tier = Limit(source.Tier),
            Enchant = Limit(source.Enchant),
            Tags = source.Tags.Select(tag => Limit(tag) ?? string.Empty).ToList(),
            Attributes = new Dictionary<string, int>(source.Attributes),
        };

    private static List<BundleBattleProjectionV5> BuildProjections(
        RunPayloadV5 payload,
        string playerAccountId
    ) =>
        payload
            .Battles.Where(RunBundleV5Contract.IsReplayable)
            .Where(battle => IsValidProjectionCandidate(battle, playerAccountId))
            .OrderByDescending(battle => ParseTimestamp(battle.Facts.RecordedAtUtc))
            .ThenByDescending(battle => battle.BattleId, StringComparer.Ordinal)
            .Take(BundleLimitsV5.MaxBattlesPerBundle)
            .Select(battle => MapProjection(battle, playerAccountId))
            .OrderBy(projection => projection.RecordedAtMs)
            .ThenBy(projection => projection.BattleId, StringComparer.Ordinal)
            .ToList();

    private static bool IsValidProjectionCandidate(RunBattleV5 battle, string playerAccountId) =>
        Identifier.IsMatch(battle.BattleId)
        && ParseTimestamp(battle.Facts.RecordedAtUtc) >= 0
        && battle.Facts.Day is >= 0
        && battle.Facts.Hour is >= 0
        && NormalizeIdentifier(battle.Participants.Player.AccountId) == playerAccountId
        && NormalizeIdentifier(battle.Participants.Opponent.AccountId) != null;

    private static BundleBattleProjectionV5 MapProjection(
        RunBattleV5 battle,
        string playerAccountId
    ) =>
        new()
        {
            BattleId = battle.BattleId,
            RecordedAtMs = ParseTimestamp(battle.Facts.RecordedAtUtc),
            Day = battle.Facts.Day!.Value,
            Hour = battle.Facts.Hour!.Value,
            EncounterId = Limit(battle.Facts.EncounterId),
            CombatKind = NormalizeIdentifier(battle.Facts.CombatKind) ?? "pvp",
            Result = NormalizeIdentifier(battle.Facts.Result) ?? "unknown",
            WinnerCombatantId = Limit(battle.Facts.WinnerCombatantId),
            LoserCombatantId = Limit(battle.Facts.LoserCombatantId),
            IsFinalBattle = battle.Facts.IsFinalBattle,
            Player = MapCombatant(battle.Participants.Player, playerAccountId),
            Opponent = MapCombatant(
                battle.Participants.Opponent,
                NormalizeIdentifier(battle.Participants.Opponent.AccountId)!
            ),
        };

    private static BundleCombatantProjectionV5 MapCombatant(
        BattleParticipantV5 participant,
        string accountId
    ) =>
        new()
        {
            AccountId = accountId,
            DisplayName = Limit(participant.DisplayName) ?? string.Empty,
            HeroId = null,
            HeroName = Limit(participant.HeroName),
            Rank = Limit(participant.Rank),
            Rating = NonNegativeLong(participant.Rating),
            Level = NonNegativeLong(participant.Level),
            Prestige = NonNegativeLong(participant.Prestige),
            Victories = NonNegativeLong(participant.Victories),
        };

    private static void ApplySizeBudget(
        RunPayloadV5 payload,
        List<BundleBattleProjectionV5> projections
    )
    {
        if (EncodedSize(payload) <= BundleLimitsV5.MaxRunBytes)
            return;
        var projected = projections
            .Select(value => value.BattleId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var battle in payload.Battles.Where(value => !projected.Contains(value.BattleId)))
        {
            battle.Snapshots = null;
            battle.Replay = null;
            payload.ReplayableBattleIds.Remove(battle.BattleId);
            AddDegradation(payload, "non_projection_replay_trimmed", battle.BattleId);
        }
        if (EncodedSize(payload) <= BundleLimitsV5.MaxRunBytes)
            return;

        while (projections.Count > 0 && EncodedSize(payload) > BundleLimitsV5.MaxRunBytes)
        {
            var projection = projections[0];
            projections.RemoveAt(0);
            var battle = payload.Battles.First(value => value.BattleId == projection.BattleId);
            battle.Snapshots = null;
            battle.Replay = null;
            payload.ReplayableBattleIds.Remove(battle.BattleId);
            AddDegradation(payload, "projection_battle_trimmed", battle.BattleId);
        }
        foreach (
            var battle in payload.Battles.Where(value =>
                value.Snapshots != null && value.Replay == null
            )
        )
            battle.Snapshots = null;
        if (EncodedSize(payload) <= BundleLimitsV5.MaxRunBytes)
            return;

        for (
            var index = 0;
            index < payload.Events.Count && EncodedSize(payload) > BundleLimitsV5.MaxRunBytes;

        )
        {
            if (IsTerminalEvent(payload.Events[index].Kind))
            {
                index++;
                continue;
            }
            payload.Events.RemoveAt(index);
            payload.Degradation.EventsOmitted++;
            if (!payload.Degradation.Categories.Contains("events_trimmed"))
                payload.Degradation.Categories.Add("events_trimmed");
        }
    }

    private static void AddDegradation(RunPayloadV5 payload, string category, string battleId)
    {
        if (!payload.Degradation.Categories.Contains(category))
            payload.Degradation.Categories.Add(category);
        if (!payload.Degradation.ReplayOmittedBattleIds.Contains(battleId))
            payload.Degradation.ReplayOmittedBattleIds.Add(battleId);
    }

    private static bool IsTerminalEvent(string kind) =>
        kind is "run_started" or "run_completed" or "run_abandoned";

    private static int EncodedSize(RunPayloadV5 payload) =>
        RunPayloadV5Codec.Encode(payload).Length;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static int? NullableInt(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? NonNegative(int? value) => value is >= 0 ? value : null;

    private static long? NonNegativeLong(int? value) => value is >= 0 ? value.Value : null;

    private static string? Limit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static string? NormalizeIdentifier(string? value)
    {
        var normalized = value?.Trim();
        return normalized != null && Identifier.IsMatch(normalized) ? normalized : null;
    }

    private static string NormalizeResult(string? result) =>
        NormalizeIdentifier(result?.ToLowerInvariant()) ?? "unknown";

    private static long ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp
        )
            ? timestamp.ToUnixTimeMilliseconds()
            : -1;
}

internal sealed record RunPayloadComposition(
    RunPayloadV5 Payload,
    List<BundleBattleProjectionV5> Projections,
    byte[] EncodedPayload
);

internal sealed class BundleCompositionException : Exception
{
    internal BundleCompositionException(string code)
        : base(code) => Code = code;

    internal string Code { get; }
}
