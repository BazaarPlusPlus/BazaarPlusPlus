#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.ModApi.Bundle;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

internal static class GhostManifestProjection
{
    internal static PvpBattleManifest BuildRecorderPerspectiveManifest(
        GhostBundleReference reference,
        string runId,
        RunBattleV5 battle
    ) =>
        new()
        {
            BattleId = reference.LocalBattleId,
            RunId = runId,
            RecordedAtUtc = DateTimeOffset.Parse(battle.Facts.RecordedAtUtc),
            CombatKind = battle.Facts.CombatKind,
            Day = battle.Facts.Day,
            Hour = battle.Facts.Hour,
            EncounterId = battle.Facts.EncounterId,
            Participants = new PvpBattleParticipants
            {
                PlayerName = battle.Participants.Player.DisplayName,
                PlayerAccountId = battle.Participants.Player.AccountId,
                PlayerHero = battle.Participants.Player.HeroName,
                PlayerRank = battle.Participants.Player.Rank,
                PlayerRating = battle.Participants.Player.Rating,
                PlayerLevel = battle.Participants.Player.Level,
                PlayerPrestige = battle.Participants.Player.Prestige,
                PlayerIncome = battle.Participants.Player.Income,
                PlayerGold = battle.Participants.Player.Gold,
                PlayerVictories = battle.Participants.Player.Victories,
                OpponentName = battle.Participants.Opponent.DisplayName,
                OpponentAccountId = battle.Participants.Opponent.AccountId,
                OpponentHero = battle.Participants.Opponent.HeroName,
                OpponentRank = battle.Participants.Opponent.Rank,
                OpponentRating = battle.Participants.Opponent.Rating,
                OpponentLevel = battle.Participants.Opponent.Level,
                OpponentPrestige = battle.Participants.Opponent.Prestige,
                OpponentVictories = battle.Participants.Opponent.Victories,
            },
            Outcome = new PvpBattleOutcome
            {
                Result = battle.Facts.Result,
                WinnerCombatantId = battle.Facts.WinnerCombatantId,
                LoserCombatantId = battle.Facts.LoserCombatantId,
            },
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = BuildCapture(battle.Snapshots!, "player_hand"),
                PlayerSkills = BuildCapture(battle.Snapshots!, "player_skills"),
                OpponentHand = BuildCapture(battle.Snapshots!, "opponent_hand"),
                OpponentSkills = BuildCapture(battle.Snapshots!, "opponent_skills"),
            },
        };

    internal static PvpBattleManifest SwapPerspective(PvpBattleManifest manifest)
    {
        var participants = manifest.Participants;
        (participants.PlayerName, participants.OpponentName) = (
            participants.OpponentName,
            participants.PlayerName
        );
        (participants.PlayerAccountId, participants.OpponentAccountId) = (
            participants.OpponentAccountId,
            participants.PlayerAccountId
        );
        (participants.PlayerHero, participants.OpponentHero) = (
            participants.OpponentHero,
            participants.PlayerHero
        );
        (participants.PlayerRank, participants.OpponentRank) = (
            participants.OpponentRank,
            participants.PlayerRank
        );
        (participants.PlayerRating, participants.OpponentRating) = (
            participants.OpponentRating,
            participants.PlayerRating
        );
        (participants.PlayerLevel, participants.OpponentLevel) = (
            participants.OpponentLevel,
            participants.PlayerLevel
        );
        (participants.PlayerPrestige, participants.OpponentPrestige) = (
            participants.OpponentPrestige,
            participants.PlayerPrestige
        );
        (participants.PlayerVictories, participants.OpponentVictories) = (
            participants.OpponentVictories,
            participants.PlayerVictories
        );
        participants.PlayerIncome = null;
        participants.PlayerGold = null;

        var snapshots = manifest.Snapshots;
        (snapshots.PlayerHand, snapshots.OpponentHand) = (
            snapshots.OpponentHand,
            snapshots.PlayerHand
        );
        (snapshots.PlayerSkills, snapshots.OpponentSkills) = (
            snapshots.OpponentSkills,
            snapshots.PlayerSkills
        );

        var outcome = manifest.Outcome;
        outcome.Result = GhostBattleLocalProjector.ProjectResultToLocal(outcome.Result);
        outcome.WinnerCombatantId = GhostBattleLocalProjector.ProjectCombatantIdToLocal(
            outcome.WinnerCombatantId
        );
        outcome.LoserCombatantId = GhostBattleLocalProjector.ProjectCombatantIdToLocal(
            outcome.LoserCombatantId
        );

        return manifest;
    }

    private static PvpBattleCardSetCapture BuildCapture(
        BattleCardSnapshotsV5 snapshots,
        string label
    )
    {
        var source = snapshots.CardSets.First(set =>
            string.Equals(set.Label, label, StringComparison.Ordinal)
        );
        return new PvpBattleCardSetCapture
        {
            Status = ParseEnum(source.Status, PvpBattleCaptureStatus.Missing),
            Source = ParseEnum(source.Source, PvpBattleCaptureSource.Unknown),
            Items = source.Items.Select(MapCard).ToList(),
        };
    }

    private static PvpBattleCardSnapshot MapCard(BattleCardV5 item) =>
        new()
        {
            InstanceId = item.InstanceId,
            TemplateId = item.TemplateId,
            Type = (BazaarGameShared.Domain.Core.Types.ECardType)item.Type,
            Size = (BazaarGameShared.Domain.Core.Types.ECardSize)item.Size,
            Section = item.Section.HasValue
                ? (BazaarGameShared.Domain.Core.Types.EInventorySection?)item.Section.Value
                : null,
            Socket = item.Socket.HasValue
                ? (BazaarGameShared.Domain.Core.Types.EContainerSocketId?)item.Socket.Value
                : null,
            Name = item.Name,
            Tier = item.Tier,
            Enchant = item.Enchant,
            Tags = item.Tags.ToList(),
            Attributes = new Dictionary<string, int>(item.Attributes),
        };

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct =>
        !string.IsNullOrWhiteSpace(value)
        && Enum.TryParse<TEnum>(value.Trim(), true, out var parsed)
            ? parsed
            : fallback;
}
