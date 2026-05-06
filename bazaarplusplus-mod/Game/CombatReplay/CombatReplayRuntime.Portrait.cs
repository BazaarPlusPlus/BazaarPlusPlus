#nullable enable
using System;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed partial class CombatReplayRuntime
{
    private async Task EnsureReplayTemporaryOpponentPortraitAsync(PvpBattleManifest manifest)
    {
        if (_replayTemporaryOpponentPortrait != null)
        {
            _replayTemporaryOpponentPortrait.gameObject.SetActive(true);
            _replayTemporaryOpponentPortrait.ShowCard(show: true);
            return;
        }

        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay temp portrait: board manager unavailable for battle={manifest.BattleId}"
            );
            return;
        }

        var hero = Data.SimPvpOpponent?.Hero;
        if (!hero.HasValue || hero.Value == EHero.Common)
        {
            if (!TryParseHeroName(manifest.Participants.OpponentHero, out var parsedHero))
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    $"Replay temp portrait: opponent hero unavailable for battle={manifest.BattleId}"
                );
                return;
            }

            hero = parsedHero;
        }

        var collectionManager = Services.Get<CollectionManager>();
        if (collectionManager == null)
            throw new InvalidOperationException("CollectionManager is unavailable.");

        var loadout =
            Data.SimPvpOpponent?.PlayerLoadout
            ?? new BazaarCollectionLoadout
            {
                accountId = manifest.Participants.OpponentAccountId ?? string.Empty,
                heroSkinIds = Array.Empty<string>(),
                cardSkinIds = Array.Empty<string>(),
            };

#pragma warning disable CS0618
        var skinData =
            await collectionManager.GetEquippedHeroSkin(hero.Value, loadout)
            ?? await collectionManager.GetEquippedHeroSkin(hero.Value);
#pragma warning restore CS0618
        if (skinData == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay temp portrait: skin load returned null for hero={hero.Value} battle={manifest.BattleId}"
            );
            return;
        }

        var anchor = boardManager.GetAnchor(AnchorSide.Opponent, AnchorType.Portrait);
        var portraitController = await LoadReplayHeroPortraitAsync(
            skinData,
            ResolveReplayPortraitTier(manifest),
            anchor
        );
        if (portraitController == null)
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay temp portrait: portrait load failed for hero={hero.Value} battle={manifest.BattleId}"
            );
            return;
        }

        if (Data.CurrentEncounterController != null)
            Data.CurrentEncounterController.ShowCard(show: false);

        portraitController.gameObject.name = "ReplayOpponentPortrait";
        portraitController.gameObject.SetActive(true);
        portraitController.ShowCard(show: true);
        _replayTemporaryOpponentPortrait = portraitController;
    }

    private void CleanupReplayOpponentPortrait()
    {
        if (_replayTemporaryOpponentPortrait != null)
        {
            try
            {
                Destroy(_replayTemporaryOpponentPortrait.gameObject);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayRuntime",
                    $"Replay temp portrait cleanup failed: {ex.Message}"
                );
            }

            _replayTemporaryOpponentPortrait = null;
        }

        if (Data.CurrentEncounterController != null)
        {
            Data.CurrentEncounterController.gameObject.SetActive(true);
            Data.CurrentEncounterController.ShowCard(show: true);
        }
    }

    private static async Task<EncounterController?> LoadReplayHeroPortraitAsync(
        SkinAssetDataSO skinData,
        ETier tier,
        Transform parent
    )
    {
        var boardBuilderType = typeof(BoardBuilder);
        var loadMethod = boardBuilderType.GetMethod(
            "LoadHeroPortraitAsync",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        if (loadMethod == null)
            throw new MissingMethodException(boardBuilderType.FullName, "LoadHeroPortraitAsync");

        var taskObject = loadMethod.Invoke(null, new object?[] { skinData, tier, parent, false });
        if (taskObject is Task<EncounterController> typedTask)
            return await typedTask;

        if (taskObject is not Task task)
            return taskObject as EncounterController;

        await task;
        return task.GetType()
                .GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(task) as EncounterController;
    }

    private static ETier ResolveReplayPortraitTier(PvpBattleManifest manifest)
    {
        if (
            !string.IsNullOrWhiteSpace(manifest?.Participants?.OpponentRank)
            && Enum.TryParse(manifest.Participants.OpponentRank.Trim(), true, out ETier tier)
        )
            return tier;

        return ETier.Bronze;
    }

    private void ApplyReplaySelectedHeroOverride(PvpBattleManifest manifest)
    {
        if (manifest?.Participants == null)
            return;

        if (!TryParseHeroName(manifest.Participants.PlayerHero, out var replayHero))
            return;

        _replayOriginalSelectedHero = Data.SelectedHero;
        if (_replayOriginalSelectedHero.Value == replayHero)
        {
            _replaySelectedHeroOverridden = false;
            _replayOriginalSelectedHero = null;
            return;
        }

        SetReplaySelectedHero(replayHero);
        _replaySelectedHeroOverridden = true;
    }

    private void RestoreReplaySelectedHeroOverride()
    {
        if (!_replaySelectedHeroOverridden || !_replayOriginalSelectedHero.HasValue)
            return;

        SetReplaySelectedHero(_replayOriginalSelectedHero.Value);
        _replaySelectedHeroOverridden = false;
        _replayOriginalSelectedHero = null;
    }

    private static void SetReplaySelectedHero(EHero hero)
    {
        var clientCacheType = typeof(Data).Assembly.GetType("TheBazaar.ClientCache", false);
        var runConfigField = clientCacheType?.GetField(
            "RunConfig",
            BindingFlags.Static | BindingFlags.Public
        );
        var runConfig = runConfigField?.GetValue(null);
        var setSelectedHeroMethod = runConfig
            ?.GetType()
            .GetMethod("SetSelectedHero", BindingFlags.Instance | BindingFlags.Public);
        if (setSelectedHeroMethod == null)
            throw new MissingMethodException("TheBazaar.RunConfigurationCache", "SetSelectedHero");

        setSelectedHeroMethod.Invoke(runConfig, new object[] { hero });
    }

    private static void EnsureReplayOpponentIdentity(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage
    )
    {
        if (manifest?.Participants == null)
            return;

        if (spawnMessage?.Data?.CurrentState?.PvpOpponent != null)
            return;

        if (!TryParseHeroName(manifest.Participants.OpponentHero, out var opponentHero))
        {
            BppLog.Warn(
                "CombatReplayRuntime",
                $"Replay opponent hero was unavailable for battle {manifest.BattleId}."
            );
            return;
        }

        var opponentLoadout = new BazaarCollectionLoadout
        {
            accountId = manifest.Participants.OpponentAccountId ?? string.Empty,
            heroSkinIds = Array.Empty<string>(),
            cardSkinIds = Array.Empty<string>(),
        };

        Data.SimPvpOpponent = new SimPvpOpponent(
            manifest.Participants.OpponentName,
            null,
            null,
            TryParseRank(manifest.Participants.OpponentRank),
            manifest.Participants.OpponentRating ?? 0,
            null,
            null,
            null,
            manifest.Participants.OpponentLevel,
            opponentHero,
            opponentLoadout,
            null
        );
    }

    private static bool TryParseHeroName(string? heroName, out EHero hero)
    {
        if (!string.IsNullOrWhiteSpace(heroName))
        {
            var trimmed = heroName.Trim();
            if (Enum.TryParse(trimmed, ignoreCase: true, out hero))
                return true;
        }

        hero = default;
        return false;
    }

    private static ERank? TryParseRank(string? rank)
    {
        if (!string.IsNullOrWhiteSpace(rank) && Enum.TryParse(rank.Trim(), true, out ERank parsed))
            return parsed;

        return null;
    }
}
