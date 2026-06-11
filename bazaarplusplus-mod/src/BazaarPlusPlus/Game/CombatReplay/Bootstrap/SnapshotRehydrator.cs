#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

internal static class SnapshotRehydrator
{
    internal static void RehydratePlayerCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "SnapshotRehydrator",
                $"Saved replay {manifest.BattleId} does not contain player-hand snapshots; player cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateCards(capture.Items, spawnMessage, Data.Run?.Player);
    }

    internal static void RehydrateOpponentCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "SnapshotRehydrator",
                $"Saved replay {manifest.BattleId} does not contain opponent-hand snapshots; opponent cards may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        RehydrateCards(capture.Items, spawnMessage, Data.Run?.Opponent);
    }

    internal static void RehydratePlayerSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.PlayerSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "SnapshotRehydrator",
                $"Saved replay {manifest.BattleId} does not contain player-skill snapshots; player skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSkillCards(capture.Items, spawnMessage, Data.Run?.Player);
        ReplaceSkillCollection(Data.Run?.Player, skills);
    }

    internal static void RehydrateOpponentSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        var capture = manifest.Snapshots.OpponentSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            BppLog.Warn(
                "SnapshotRehydrator",
                $"Saved replay {manifest.BattleId} does not contain opponent-skill snapshots; opponent skills may be missing. Re-capture this fight with the current mod build."
            );
            return;
        }

        var skills = RehydrateSkillCards(capture.Items, spawnMessage, Data.Run?.Opponent);
        ReplaceSkillCollection(Data.Run?.Opponent, skills);
    }

    internal static void SanitizeSpawnEvents(CombatSequenceMessages sequence)
    {
        var events = sequence.SpawnMessage?.Data?.Events;
        if (events == null || events.Count == 0)
            return;

        var removedCount = events.RemoveAll(ShouldRemoveSpawnEvent);
        if (removedCount > 0)
        {
            BppLog.Info(
                "SnapshotRehydrator",
                $"Removed {removedCount} non-combat opponent spawn events from saved replay bootstrap."
            );
        }
    }

    private static bool ShouldRemoveSpawnEvent(IGameSimEvent gameSimEvent)
    {
        return gameSimEvent
            is GameSimEventCardSpawned
            {
                CombatantId: ECombatantId.Opponent,
                Section: not EInventorySection.Hand,
            };
    }

    private static void RehydrateCards(
        IEnumerable<PvpBattleCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;
        }
    }

    private static List<SkillCard> RehydrateSkillCards(
        IEnumerable<PvpBattleCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        IPlayer? owner
    )
    {
        var skills = new List<SkillCard>();
        foreach (var snapshot in snapshots.Where(snapshot => snapshot != null))
        {
            if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;

            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                snapshot.Type
            );
            if (spawnMessage.Data.Cards.TryGetValue(snapshot.InstanceId, out var simUpdate))
                card.Update(simUpdate);

            ApplySnapshotFallback(card, snapshot);
            card.Size = snapshot.Size;
            card.Owner = owner;
            card.Section = snapshot.Section;
            card.LeftSocketId = snapshot.Socket;

            if (card is SkillCard skillCard)
                skills.Add(skillCard);
        }

        return skills;
    }

    private static void ApplySnapshotFallback(Card card, PvpBattleCardSnapshot snapshot)
    {
        if (snapshot.Attributes != null && snapshot.Attributes.Count > 0)
        {
            foreach (var entry in snapshot.Attributes)
            {
                if (
                    Enum.TryParse<ECardAttributeType>(
                        entry.Key,
                        ignoreCase: false,
                        out var attributeType
                    )
                )
                    card.Attributes[attributeType] = entry.Value;
            }
        }

        if (snapshot.Tags != null && snapshot.Tags.Count > 0)
        {
            card.Tags = snapshot
                .Tags.Select(tag =>
                    Enum.TryParse<ECardTag>(tag, ignoreCase: false, out var parsedTag)
                        ? (ECardTag?)parsedTag
                        : null
                )
                .Where(tag => tag.HasValue)
                .Select(tag => tag!.Value)
                .ToHashSet();
        }

        if (
            !string.IsNullOrWhiteSpace(snapshot.Tier)
            && Enum.TryParse<ETier>(snapshot.Tier, ignoreCase: false, out var tier)
        )
            card.Tier = tier;

        if (
            card is ItemCard itemCard
            && !string.IsNullOrWhiteSpace(snapshot.Enchant)
            && Enum.TryParse<EEnchantmentType>(
                snapshot.Enchant,
                ignoreCase: false,
                out var enchantment
            )
        )
            itemCard.Enchantment = enchantment;
    }

    private static void ReplaceSkillCollection(object? combatant, IReadOnlyList<SkillCard> skills)
    {
        if (combatant == null)
            return;

        var skillsProperty = combatant
            .GetType()
            .GetProperty(
                "Skills",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (skillsProperty == null)
            return;

        if (skillsProperty.CanWrite)
        {
            skillsProperty.SetValue(combatant, skills.ToList());
            return;
        }

        if (skillsProperty.GetValue(combatant) is System.Collections.IList list)
        {
            list.Clear();
            foreach (var skill in skills)
                list.Add(skill);
        }
    }
}
