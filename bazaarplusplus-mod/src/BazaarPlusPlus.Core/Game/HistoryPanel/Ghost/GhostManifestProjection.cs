#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Bundle;

namespace BazaarPlusPlus.Game.HistoryPanel.Ghost;

public static class GhostManifestProjection
{
    internal static PvpBattleManifest BuildRecorderPerspectiveManifest(
        GhostBundleReference reference,
        string runId,
        RunBattleV6 battle,
        IPvpBattleEvidenceBinding evidence
    ) =>
        new()
        {
            BattleId = reference.LocalBattleId,
            RunId = runId,
            RecordedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(battle.Facts.RecordedAtMs),
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
                PlayerHand = BuildCapture(battle.Snapshots!, "player_hand", evidence),
                PlayerSkills = BuildCapture(battle.Snapshots!, "player_skills", evidence),
                OpponentHand = BuildCapture(battle.Snapshots!, "opponent_hand", evidence),
                OpponentSkills = BuildCapture(battle.Snapshots!, "opponent_skills", evidence),
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
        BattleCardSnapshotsV6 snapshots,
        string label,
        IPvpBattleEvidenceBinding evidence
    )
    {
        var source = snapshots.CardSets.First(set =>
            string.Equals(set.Label, label, StringComparison.Ordinal)
        );
        return new PvpBattleCardSetCapture
        {
            Status = ParseEnum(source.Status, PvpBattleCaptureStatus.Missing),
            Source = ParseEnum(source.Source, PvpBattleCaptureSource.Unknown),
            Items = source
                .Items.Select(item => MapCard(item, evidence))
                .OfType<PvpBattleCardSnapshot>()
                .ToList(),
        };
    }

    // A Bundle card carries the game's integers. A type, or a non-zero size, the facets lack drops
    // the card; size 0 is a card the game gave no size; a section or socket the facets lack reads
    // as none.
    private static PvpBattleCardSnapshot? MapCard(
        BattleCardV6 item,
        IPvpBattleEvidenceBinding evidence
    )
    {
        if (!TryFacet<CardType>(evidence, PvpCardEnum.CardType, item.Type, out var type))
            return Dropped(PvpCardEnum.CardType, item.Type);
        CardSize? size = null;
        if (item.Size != 0)
        {
            if (!TryFacet<CardSize>(evidence, PvpCardEnum.CardSize, item.Size, out var known))
                return Dropped(PvpCardEnum.CardSize, item.Size);
            size = known;
        }
        return new PvpBattleCardSnapshot
        {
            InstanceId = item.InstanceId,
            TemplateId = item.TemplateId,
            Type = type,
            Size = size,
            Section =
                item.Section is { } section
                && TryFacet<CardSection>(
                    evidence,
                    PvpCardEnum.InventorySection,
                    section,
                    out var cardSection
                )
                    ? cardSection
                    : null,
            Socket =
                item.Socket is { } socket
                && TryFacet<CardSocket>(
                    evidence,
                    PvpCardEnum.ContainerSocket,
                    socket,
                    out var cardSocket
                )
                    ? cardSocket
                    : null,
            Name = item.Name,
            Tier = item.Tier,
            Enchant = item.Enchant,
            Tags = item.Tags.ToList(),
            Attributes = new Dictionary<string, int>(item.Attributes),
        };
    }

    private static bool TryFacet<TFacet>(
        IPvpBattleEvidenceBinding evidence,
        PvpCardEnum kind,
        int value,
        out TFacet facet
    )
        where TFacet : struct, Enum
    {
        facet = default;
        return evidence.TryCardEnumName(kind, value, out var name)
            && PvpBattleSnapshotsCodec.TryParseFacet(name, out facet);
    }

    private static PvpBattleCardSnapshot? Dropped(PvpCardEnum kind, int value)
    {
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.HistoryPanel,
                "history_panel.ghost_manifest.card_dropped",
                storm: ["kind"]
            ),
            ("kind", kind),
            ("value", value)
        );
        return null;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct =>
        !string.IsNullOrWhiteSpace(value)
        && Enum.TryParse<TEnum>(value.Trim(), true, out var parsed)
            ? parsed
            : fallback;
}
