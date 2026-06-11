#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BazaarPlusPlus.ModApi.Models;

public sealed class RunBundleUploadRequest
{
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonProperty("player_account_id")]
    public string PlayerAccountId { get; set; } = string.Empty;

    [JsonProperty("submitted_at_utc")]
    public string SubmittedAtUtc { get; set; } = string.Empty;

    [JsonProperty("artifact_codec")]
    public string ArtifactCodec { get; set; } = "application/x-bpp-runbundle+msgpack+gzip";

    [JsonProperty("run_projection")]
    public RunProjection RunProjection { get; set; } = new();

    [JsonProperty("battle_projections")]
    public List<BattleProjection> BattleProjections { get; set; } = new();
}

public sealed class RunProjection
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

}

public sealed class BattleProjection
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

    [JsonProperty("player_prestige")]
    public int? PlayerPrestige { get; set; }

    [JsonProperty("player_victories")]
    public int? PlayerVictories { get; set; }

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

    [JsonProperty("opponent_prestige")]
    public int? OpponentPrestige { get; set; }

    [JsonProperty("opponent_victories")]
    public int? OpponentVictories { get; set; }

    [JsonProperty("result")]
    public string? Result { get; set; }

    [JsonProperty("winner_combatant_id")]
    public string? WinnerCombatantId { get; set; }

    [JsonProperty("loser_combatant_id")]
    public string? LoserCombatantId { get; set; }

    [JsonProperty("is_final_battle")]
    public bool IsFinalBattle { get; set; }
}

public sealed class RunArtifact
{
    [JsonProperty("run_id")]
    public string RunId { get; set; } = string.Empty;

    [JsonProperty("battles")]
    public List<RunArtifactBattle> Battles { get; set; } = new();
}

public sealed class RunArtifactBattle
{
    [JsonProperty("battle_id")]
    public string BattleId { get; set; } = string.Empty;

    [JsonProperty("manifest")]
    public BattleManifestArtifact Manifest { get; set; } = new();

    [JsonProperty("participants")]
    public BattleParticipantsArtifact Participants { get; set; } = new();

    [JsonProperty("snapshots")]
    public BattleSnapshotsArtifact Snapshots { get; set; } = new();

    [JsonProperty("replay_payload")]
    public ReplayPayloadArtifact ReplayPayload { get; set; } = new();
}

public sealed class BattleManifestArtifact
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

public sealed class BattleParticipantsArtifact
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

    [JsonProperty("player_prestige")]
    public int? PlayerPrestige { get; set; }

    [JsonProperty("player_victories")]
    public int? PlayerVictories { get; set; }

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

    [JsonProperty("opponent_prestige")]
    public int? OpponentPrestige { get; set; }

    [JsonProperty("opponent_victories")]
    public int? OpponentVictories { get; set; }
}

public sealed class BattleSnapshotsArtifact
{
    [JsonProperty("card_sets")]
    public List<CardSetCaptureArtifact> CardSets { get; set; } = new();
}

public sealed class CardSetCaptureArtifact
{
    [JsonProperty("label")]
    public string Label { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string? Status { get; set; }

    [JsonProperty("source")]
    public string? Source { get; set; }

    [JsonProperty("items")]
    public List<CardSetItemArtifact> Items { get; set; } = new();
}

/// <summary>
/// Wire-format representation of a card snapshot. Uses primitive/string types so this
/// model can live in ModApi without game-assembly references. The main assembly maps
/// these to PvpBattleCardSnapshot after deserialization.
/// </summary>
public sealed class CardSetItemArtifact
{
    [JsonProperty("instance_id")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonProperty("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonProperty("type")]
    public int Type { get; set; }

    [JsonProperty("size")]
    public int Size { get; set; }

    [JsonProperty("section")]
    public int? Section { get; set; }

    [JsonProperty("socket")]
    public int? Socket { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("tier")]
    public string? Tier { get; set; }

    [JsonProperty("enchant")]
    public string? Enchant { get; set; }

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("attributes")]
    public Dictionary<string, int> Attributes { get; set; } = new();
}

public sealed class ReplayPayloadArtifact
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
