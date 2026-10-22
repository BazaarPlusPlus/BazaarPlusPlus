#nullable enable
using System.Text.RegularExpressions;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.BundlePipeline;

/// <summary>
/// Composes one run's Run segment and projections from the history database, the stored board
/// captures (<see cref="PvpBattleCardSetDocument"/>), and the local Replay Payloads. The host
/// supplies only the game-typed reads, through <see cref="IPvpBattleEvidenceBinding"/>.
/// </summary>
public sealed class RunPayloadComposer : IRunPayloadComposer
{
    private const string ItemCardType = "Item";
    private const string SkillCardType = "Skill";
    private static readonly Regex Identifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant
    );
    private readonly string _databasePath;
    private readonly PvpBattleSqliteStore _battles;
    private readonly CombatReplayPayloadStore _replayStore;
    private readonly IPvpBattleEvidenceBinding _evidence;

    public RunPayloadComposer(
        string databasePath,
        string replayRoot,
        IPvpBattleEvidenceBinding evidence,
        SqliteOpenOptions? databaseOptions = null
    )
    {
        _databasePath = databasePath;
        _battles = new PvpBattleSqliteStore(databasePath, databaseOptions);
        _replayStore = new CombatReplayPayloadStore(replayRoot);
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
    }

    public string ResolvePlayerAccountId(string runId, string? frozenAccountId)
    {
        var frozen = NormalizeIdentifier(frozenAccountId);
        if (frozen != null)
            return frozen;
        var candidates = _battles
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

    public RunPayloadComposition Compose(string runId, string playerAccountId)
    {
        var payload = new RunPayloadV6
        {
            RunId = runId,
            PlayerAccountId = playerAccountId,
            Run = ReadRunFacts(runId),
            Events = ReadEvents(runId),
        };
        var manifests = _battles
            .ListByRunId(runId)
            .OrderBy(battle => battle.RecordedAtUtc)
            .ThenBy(battle => battle.BattleId, StringComparer.Ordinal)
            .ToList();
        var finalBattleId =
            payload.Run.Status == "completed" ? manifests.LastOrDefault()?.BattleId : null;
        var replayFailures = new List<ReplayLoadFailure>();
        foreach (var manifest in manifests)
        {
            var battle = MapBattle(manifest, finalBattleId, replayFailures);
            payload.Battles.Add(battle);
            if (RunBundleV6Contract.IsReplayable(battle))
                payload.ReplayableBattleIds.Add(battle.BattleId);
            else
                payload.Degradation.ReplayOmittedBattleIds.Add(battle.BattleId);
        }

        var projections = BuildProjections(payload, playerAccountId);
        return new RunPayloadComposition(payload, projections, replayFailures);
    }

    // Encoding belongs to the payload-encode seal stage: a codec failure here is deterministic,
    // unlike a read failure while composing. The coordinator encodes the final payload itself.
    public void FitToBudget(RunPayloadComposition composition) =>
        ApplySizeBudget(composition.Payload, composition.Projections);

    private RunFactsV6 ReadRunFacts(string runId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT hero, game_mode, started_at_ms, ended_at_ms, status,
                   COALESCE(final_day, day), COALESCE(final_hour, hour), victories, losses,
                   player_rank, player_rating, final_player_rank, final_player_rating,
                   final_player_rating_delta, max_health, prestige, level, income, gold,
                   build_channel, mod_version
            FROM {RunLogSchema.RunsTableName}
            WHERE run_id = $runId AND status <> 'active'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new BundleCompositionException("source_run_missing");
        return new RunFactsV6
        {
            Hero = reader.GetString(0),
            GameMode = reader.GetString(1),
            Seed = null,
            StartedAtMs = reader.GetInt64(2),
            EndedAtMs = reader.IsDBNull(3) ? null : reader.GetInt64(3),
            Status = reader.GetString(4),
            Day = NonNegative(NullableInt(reader, 5)),
            Hour = NonNegative(NullableInt(reader, 6)),
            Victories = NonNegative(NullableInt(reader, 7)),
            Losses = NonNegative(NullableInt(reader, 8)),
            PlayerRank = Limit(NullableString(reader, 9)),
            PlayerRating = NonNegative(NullableInt(reader, 10)),
            FinalPlayerRank = Limit(NullableString(reader, 11)),
            FinalPlayerRating = NonNegative(NullableInt(reader, 12)),
            FinalPlayerRatingDelta = NullableInt(reader, 13),
            MaxHealth = NonNegative(NullableInt(reader, 14)),
            Prestige = NonNegative(NullableInt(reader, 15)),
            Level = NonNegative(NullableInt(reader, 16)),
            Income = NonNegative(NullableInt(reader, 17)),
            Gold = NonNegative(NullableInt(reader, 18)),
            BuildChannel = Limit(NullableString(reader, 19)),
            ModVersion = Limit(NullableString(reader, 20)) ?? string.Empty,
        };
    }

    private List<RunEventV6> ReadEvents(string runId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT seq, ts_at_ms, kind, payload_json
            FROM {RunLogSchema.RunEventsTableName}
            WHERE run_id = $runId
            ORDER BY seq ASC;
            """;
        command.Parameters.AddWithValue("$runId", runId);
        using var reader = command.ExecuteReader();
        var events = new List<RunEventV6>();
        while (reader.Read())
            events.Add(
                new RunEventV6
                {
                    Seq = reader.GetInt64(0),
                    TimestampMs = reader.GetInt64(1),
                    Kind = reader.GetString(2),
                    PayloadJson = EventPayloadBody(reader.GetString(3)),
                }
            );
        return events;
    }

    // A replay that exists but cannot be used is reported, not decided: the seal coordinator asks
    // BundleSealFailurePolicy whether it omits that replay or holds the whole Run.
    private RunBattleV6 MapBattle(
        PvpBattleRecord manifest,
        string? finalBattleId,
        List<ReplayLoadFailure> replayFailures
    )
    {
        BattleReplayV6? replay = null;
        var loaded = _replayStore.LoadDetailed(manifest.BattleId);
        if (loaded.Status == FileBackedPayloadLoadStatus.Unreadable)
            replayFailures.Add(new(manifest.BattleId, Unreadable: true, loaded.Exception));
        else if (loaded.Status == FileBackedPayloadLoadStatus.Invalid)
            replayFailures.Add(new(manifest.BattleId, Unreadable: false, loaded.Exception));
        else if (loaded.Status == FileBackedPayloadLoadStatus.Loaded && loaded.Payload != null)
        {
            try
            {
                _evidence.VerifyReplay(loaded.Payload);
                replay = new BattleReplayV6
                {
                    Version = loaded.Payload.Version,
                    SpawnMessageBytes = loaded.Payload.SpawnMessageBytes.ToArray(),
                    CombatMessageBytes = loaded.Payload.CombatMessageBytes.ToArray(),
                    DespawnMessageBytes = loaded.Payload.DespawnMessageBytes.ToArray(),
                };
            }
            catch (Exception ex)
            {
                replayFailures.Add(new(manifest.BattleId, Unreadable: false, ex));
            }
        }

        var playerHand = ReadCardSet(manifest.Snapshots?.PlayerHand);
        var playerSkills = ReadCardSet(manifest.Snapshots?.PlayerSkills);
        var opponentHand = ReadCardSet(manifest.Snapshots?.OpponentHand);
        var opponentSkills = ReadCardSet(manifest.Snapshots?.OpponentSkills);
        return new RunBattleV6
        {
            BattleId = manifest.BattleId,
            Facts = new BattleFactsV6
            {
                RecordedAtMs = manifest.RecordedAtUtc.ToUnixTimeMilliseconds(),
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
            Participants = new BattleParticipantsV6
            {
                Player = MapParticipant(manifest.Participants, playerHand, playerSkills, true),
                Opponent = MapParticipant(
                    manifest.Participants,
                    opponentHand,
                    opponentSkills,
                    player: false
                ),
            },
            Snapshots = new BattleCardSnapshotsV6
            {
                CardSets =
                [
                    MapCardSet("player_hand", playerHand),
                    MapCardSet("player_skills", playerSkills),
                    MapCardSet("opponent_hand", opponentHand),
                    MapCardSet("opponent_skills", opponentSkills),
                ],
            },
            Replay = replay,
        };
    }

    // A battle without a snapshot row has every set Missing, as a capture that never ran.
    private static PvpBattleCardSetDocument ReadCardSet(string? document) =>
        document == null
            ? new PvpBattleCardSetDocument()
            : PvpBattleCardSetDocument.Deserialize(document);

    private BattleParticipantV6 MapParticipant(
        PvpBattleParticipants participants,
        PvpBattleCardSetDocument hand,
        PvpBattleCardSetDocument skills,
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
            HandItemCount = CountCards(hand, ItemCardType),
            SkillCount = CountCards(skills, SkillCardType),
        };

    // Counted from the captured set so they survive ApplySizeBudget dropping the card sets; a
    // Missing set was never captured, so its count is unknown rather than zero.
    private int? CountCards(PvpBattleCardSetDocument source, string cardType)
    {
        if (source.Status == PvpBattleCaptureStatus.Missing)
            return null;
        var type = _evidence.CardEnumValue(PvpCardEnum.CardType, cardType);
        return source.Items.Count(card =>
            _evidence.CardEnumValue(PvpCardEnum.CardType, card.Type) == type
        );
    }

    private BattleCardSetV6 MapCardSet(string label, PvpBattleCardSetDocument source) =>
        new()
        {
            Label = label,
            Status = source.Status.ToString(),
            Source = source.Source.ToString(),
            Items = source.Items.Select(MapCard).ToList(),
        };

    private BattleCardV6 MapCard(PvpBattleCardDocument source) =>
        new()
        {
            InstanceId = source.InstanceId,
            TemplateId = source.TemplateId,
            Type = _evidence.CardEnumValue(PvpCardEnum.CardType, source.Type),
            Size = _evidence.CardEnumValue(PvpCardEnum.CardSize, source.Size),
            Section =
                source.Section != null
                    ? _evidence.CardEnumValue(PvpCardEnum.InventorySection, source.Section)
                    : null,
            Socket =
                source.Socket != null
                    ? _evidence.CardEnumValue(PvpCardEnum.ContainerSocket, source.Socket)
                    : null,
            Name = Limit(source.Name),
            Tier = Limit(source.Tier),
            Enchant = Limit(source.Enchant),
            Tags = source.Tags.Select(tag => Limit(tag) ?? string.Empty).ToList(),
            Attributes = new Dictionary<string, int>(source.Attributes),
        };

    private static List<BundleBattleProjectionV6> BuildProjections(
        RunPayloadV6 payload,
        string playerAccountId
    ) =>
        payload
            .Battles.Where(RunBundleV6Contract.IsReplayable)
            .Where(battle => IsValidProjectionCandidate(battle, playerAccountId))
            .OrderByDescending(battle => battle.Facts.RecordedAtMs)
            .ThenByDescending(battle => battle.BattleId, StringComparer.Ordinal)
            .Take(BundleLimitsV6.MaxBattlesPerBundle)
            .Select(battle => MapProjection(battle, playerAccountId))
            .OrderBy(projection => projection.RecordedAtMs)
            .ThenBy(projection => projection.BattleId, StringComparer.Ordinal)
            .ToList();

    private static bool IsValidProjectionCandidate(RunBattleV6 battle, string playerAccountId) =>
        Identifier.IsMatch(battle.BattleId)
        && battle.Facts.RecordedAtMs >= 0
        && battle.Facts.Day is >= 0
        && battle.Facts.Hour is >= 0
        && NormalizeIdentifier(battle.Participants.Player.AccountId) == playerAccountId
        && NormalizeIdentifier(battle.Participants.Opponent.AccountId) != null
        && NormalizeIdentifier(battle.Facts.WinnerCombatantId) != null
        && NormalizeIdentifier(battle.Facts.LoserCombatantId) != null;

    private static BundleBattleProjectionV6 MapProjection(
        RunBattleV6 battle,
        string playerAccountId
    ) =>
        new()
        {
            BattleId = battle.BattleId,
            RecordedAtMs = battle.Facts.RecordedAtMs,
            Day = battle.Facts.Day!.Value,
            Hour = battle.Facts.Hour!.Value,
            EncounterId = Limit(battle.Facts.EncounterId),
            CombatKind = NormalizeIdentifier(battle.Facts.CombatKind) ?? "pvp",
            Result = NormalizeIdentifier(battle.Facts.Result) ?? "unknown",
            WinnerCombatantId = NormalizeIdentifier(battle.Facts.WinnerCombatantId)!,
            LoserCombatantId = NormalizeIdentifier(battle.Facts.LoserCombatantId)!,
            IsFinalBattle = battle.Facts.IsFinalBattle,
            Player = MapCombatant(battle.Participants.Player, playerAccountId),
            Opponent = MapCombatant(
                battle.Participants.Opponent,
                NormalizeIdentifier(battle.Participants.Opponent.AccountId)!
            ),
        };

    private static BundleCombatantProjectionV6 MapCombatant(
        BattleParticipantV6 participant,
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
        RunPayloadV6 payload,
        List<BundleBattleProjectionV6> projections
    )
    {
        if (EncodedSize(payload) <= BundleLimitsV6.MaxRunBytes)
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
        if (EncodedSize(payload) <= BundleLimitsV6.MaxRunBytes)
            return;

        while (projections.Count > 0 && EncodedSize(payload) > BundleLimitsV6.MaxRunBytes)
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
        if (EncodedSize(payload) <= BundleLimitsV6.MaxRunBytes)
            return;

        for (
            var index = 0;
            index < payload.Events.Count && EncodedSize(payload) > BundleLimitsV6.MaxRunBytes;

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

    private static void AddDegradation(RunPayloadV6 payload, string category, string battleId)
    {
        if (!payload.Degradation.Categories.Contains(category))
            payload.Degradation.Categories.Add(category);
        if (!payload.Degradation.ReplayOmittedBattleIds.Contains(battleId))
            payload.Degradation.ReplayOmittedBattleIds.Add(battleId);
    }

    private static bool IsTerminalEvent(string kind) =>
        kind is "run_started" or "run_completed" or "run_abandoned";

    private static int EncodedSize(RunPayloadV6 payload) =>
        RunPayloadV6Codec.Encode(payload).Length;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static int? NullableInt(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    // The RunEventV6 envelope owns run_id, seq, ts, and kind; the stored payload_json repeats them
    // (ts as text), so the Run segment carries only the remaining fields, in stored order.
    private static readonly string[] EventEnvelopeKeys = ["run_id", "seq", "ts", "kind"];

    internal static string EventPayloadBody(string payloadJson)
    {
        using var reader = new JsonTextReader(new StringReader(payloadJson))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
        };
        var body = JObject.Load(reader);
        foreach (var key in EventEnvelopeKeys)
            body.Remove(key);
        return body.ToString(Formatting.None);
    }

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
}
