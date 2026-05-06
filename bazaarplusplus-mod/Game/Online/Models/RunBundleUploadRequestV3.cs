#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Game.CombatReplay;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.Online.Models;

internal sealed class RunBundleUploadRequestV3
{
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonProperty("player_account_id")]
    public string PlayerAccountId { get; set; } = string.Empty;

    [JsonProperty("submitted_at_utc")]
    public string SubmittedAtUtc { get; set; } = string.Empty;

    [JsonProperty("artifact_codec")]
    public string ArtifactCodec { get; set; } = "application/json";

    [JsonIgnore]
    public byte[] ArtifactBytes { get; set; } = Array.Empty<byte>();

    [JsonProperty("artifact_bytes")]
    public string ArtifactBytesBase64
    {
        get => Convert.ToBase64String(ArtifactBytes);
        set =>
            ArtifactBytes = string.IsNullOrEmpty(value)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(value);
    }

    [JsonProperty("run_projection")]
    public RunProjectionV3 RunProjection { get; set; } = new();

    [JsonProperty("battle_projections")]
    public List<BattleProjectionV3> BattleProjections { get; set; } = new();
}

internal sealed class RunProjectionV3
{
    [JsonProperty("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("hero_id")]
    public string? HeroId { get; set; }

    [JsonProperty("hero_name")]
    public string? HeroName { get; set; }

    [JsonProperty("player_rank")]
    public string? PlayerRank { get; set; }

    [JsonProperty("player_rating")]
    public int? PlayerRating { get; set; }

    [JsonProperty("player_position")]
    public int? PlayerPosition { get; set; }

    [JsonProperty("started_at_utc")]
    public string? StartedAtUtc { get; set; }

    [JsonProperty("ended_at_utc")]
    public string EndedAtUtc { get; set; } = string.Empty;

    [JsonProperty("final_day")]
    public int? FinalDay { get; set; }

    [JsonProperty("final_wins")]
    public int? FinalWins { get; set; }

    [JsonProperty("final_losses")]
    public int? FinalLosses { get; set; }

    [JsonProperty("final_player_rank")]
    public string? FinalPlayerRank { get; set; }

    [JsonProperty("final_player_rating")]
    public int? FinalPlayerRating { get; set; }

    [JsonProperty("final_player_position")]
    public int? FinalPlayerPosition { get; set; }

    [JsonProperty("battles")]
    public List<BattleProjectionV3> Battles { get; set; } = new();
}

internal sealed class BattleProjectionV3
{
    [JsonProperty("battle_id")]
    public string BattleId { get; set; } = string.Empty;

    [JsonProperty("recorded_at_utc")]
    public string RecordedAtUtc { get; set; } = string.Empty;

    [JsonProperty("run_id")]
    public string? RunId { get; set; }

    [JsonProperty("day")]
    public int? Day { get; set; }

    [JsonProperty("player_name")]
    public string? PlayerName { get; set; }

    [JsonProperty("player_account_id")]
    public string? PlayerAccountId { get; set; }

    [JsonProperty("player_hero")]
    public string? PlayerHero { get; set; }

    [JsonProperty("player_rank")]
    public string? PlayerRank { get; set; }

    [JsonProperty("player_rating")]
    public int? PlayerRating { get; set; }

    [JsonProperty("player_level")]
    public int? PlayerLevel { get; set; }

    [JsonProperty("opponent_name")]
    public string? OpponentName { get; set; }

    [JsonProperty("opponent_account_id")]
    public string? OpponentAccountId { get; set; }

    [JsonProperty("opponent_hero")]
    public string? OpponentHero { get; set; }

    [JsonProperty("opponent_rank")]
    public string? OpponentRank { get; set; }

    [JsonProperty("opponent_rating")]
    public int? OpponentRating { get; set; }

    [JsonProperty("opponent_level")]
    public int? OpponentLevel { get; set; }

    [JsonProperty("result")]
    public string? Result { get; set; }

    [JsonProperty("replay_available")]
    public bool ReplayAvailable { get; set; }
}

public sealed class RunArtifactV3
{
    [JsonProperty("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonProperty("battles")]
    public List<RunArtifactBattleV3> Battles { get; set; } = new();
}

public sealed class RunArtifactBattleV3
{
    [JsonProperty("battle_id")]
    public string BattleId { get; set; } = string.Empty;

    [JsonProperty("manifest")]
    public BattleManifestArtifactV3 Manifest { get; set; } = new();

    [JsonProperty("participants")]
    public BattleParticipantsArtifactV3 Participants { get; set; } = new();

    [JsonProperty("snapshots")]
    public BattleSnapshotsArtifactV3 Snapshots { get; set; } = new();

    [JsonProperty("replay_payload")]
    public ReplayPayloadArtifactV3 ReplayPayload { get; set; } = new();
}

public sealed class BattleManifestArtifactV3
{
    [JsonProperty("battle_id")]
    public string? BattleId { get; set; }

    [JsonProperty("recorded_at_utc")]
    public string RecordedAtUtc { get; set; } = string.Empty;

    [JsonProperty("day")]
    public int? Day { get; set; }

    [JsonProperty("hour")]
    public int? Hour { get; set; }

    [JsonProperty("encounter_id")]
    public string? EncounterId { get; set; }

    [JsonProperty("combat_kind")]
    public string? CombatKind { get; set; }

    [JsonProperty("result")]
    public string? Result { get; set; }

    [JsonProperty("winner_combatant_id")]
    public string? WinnerCombatantId { get; set; }

    [JsonProperty("loser_combatant_id")]
    public string? LoserCombatantId { get; set; }
}

public sealed class BattleParticipantsArtifactV3
{
    [JsonProperty("player_name")]
    public string? PlayerName { get; set; }

    [JsonProperty("player_account_id")]
    public string? PlayerAccountId { get; set; }

    [JsonProperty("player_hero")]
    public string? PlayerHero { get; set; }

    [JsonProperty("player_rank")]
    public string? PlayerRank { get; set; }

    [JsonProperty("player_rating")]
    public int? PlayerRating { get; set; }

    [JsonProperty("player_level")]
    public int? PlayerLevel { get; set; }

    [JsonProperty("opponent_name")]
    public string? OpponentName { get; set; }

    [JsonProperty("opponent_account_id")]
    public string? OpponentAccountId { get; set; }

    [JsonProperty("opponent_hero")]
    public string? OpponentHero { get; set; }

    [JsonProperty("opponent_rank")]
    public string? OpponentRank { get; set; }

    [JsonProperty("opponent_rating")]
    public int? OpponentRating { get; set; }

    [JsonProperty("opponent_level")]
    public int? OpponentLevel { get; set; }
}

public sealed class BattleSnapshotsArtifactV3
{
    [JsonProperty("card_sets")]
    public List<CardSetCaptureArtifactV3> CardSets { get; set; } = new();
}

public sealed class CardSetCaptureArtifactV3
{
    [JsonProperty("label")]
    public string Label { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string? Status { get; set; }

    [JsonProperty("source")]
    public string? Source { get; set; }

    [JsonProperty("items")]
    public List<CombatReplayCardSnapshot> Items { get; set; } = new();
}

public sealed class ReplayPayloadArtifactV3
{
    [JsonProperty("battle_id")]
    public string BattleId { get; set; } = string.Empty;

    [JsonProperty("version")]
    public int Version { get; set; } = 1;

    [JsonProperty("spawn_message_bytes")]
    public byte[] SpawnMessageBytes { get; set; } = [];

    [JsonProperty("combat_message_bytes")]
    public byte[] CombatMessageBytes { get; set; } = [];

    [JsonProperty("despawn_message_bytes")]
    public byte[] DespawnMessageBytes { get; set; } = [];
}
