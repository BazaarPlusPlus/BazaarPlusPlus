#nullable enable
using System;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal sealed class OpponentPortraitController
{
    private readonly Action<UnityEngine.Object> _destroyHandle;
    private EncounterController? _portrait;
    private EHero? _originalSelectedHero;
    private bool _selectedHeroOverridden;

    public OpponentPortraitController(Action<UnityEngine.Object> destroyHandle)
    {
        _destroyHandle = destroyHandle ?? throw new ArgumentNullException(nameof(destroyHandle));
    }

    public async Task EnsureTemporaryOpponentPortraitAsync(PvpBattleManifest manifest)
    {
        if (_portrait != null)
        {
            _portrait.gameObject.SetActive(true);
            _portrait.ShowCard(show: true);
            PlaybackUiState.ActiveOpponentPortrait = _portrait;
            return;
        }

        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
        {
            BppLog.Warn(
                "ReplayOpponentPortrait",
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
                    "ReplayOpponentPortrait",
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
                "ReplayOpponentPortrait",
                $"Replay temp portrait: skin load returned null for hero={hero.Value} battle={manifest.BattleId}"
            );
            return;
        }

        var anchor = boardManager.GetAnchor(AnchorSide.Opponent, AnchorType.Portrait);
        var portraitController = await LoadHeroPortraitAsync(
            skinData,
            ResolvePortraitTier(manifest),
            anchor
        );
        if (portraitController == null)
        {
            BppLog.Warn(
                "ReplayOpponentPortrait",
                $"Replay temp portrait: portrait load failed for hero={hero.Value} battle={manifest.BattleId}"
            );
            return;
        }

        if (Data.CurrentEncounterController != null)
            Data.CurrentEncounterController.ShowCard(show: false);

        portraitController.gameObject.name = "ReplayOpponentPortrait";
        portraitController.gameObject.SetActive(true);
        portraitController.ShowCard(show: true);
        _portrait = portraitController;
        PlaybackUiState.ActiveOpponentPortrait = _portrait;
    }

    public void Cleanup()
    {
        if (_portrait != null)
        {
            try
            {
                _destroyHandle(_portrait.gameObject);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "ReplayOpponentPortrait",
                    $"Replay temp portrait cleanup failed: {ex.Message}"
                );
            }

            _portrait = null;
            PlaybackUiState.ActiveOpponentPortrait = null;
        }

        if (Data.CurrentEncounterController != null)
        {
            Data.CurrentEncounterController.gameObject.SetActive(true);
            Data.CurrentEncounterController.ShowCard(show: true);
        }
    }

    public void ApplySelectedHeroOverride(PvpBattleManifest manifest)
    {
        if (manifest?.Participants == null)
            return;

        if (!TryParseHeroName(manifest.Participants.PlayerHero, out var replayHero))
            return;

        _originalSelectedHero = Data.SelectedHero;
        if (_originalSelectedHero.Value == replayHero)
        {
            _selectedHeroOverridden = false;
            _originalSelectedHero = null;
            return;
        }

        SetSelectedHero(replayHero);
        _selectedHeroOverridden = true;
    }

    public void RestoreSelectedHeroOverride()
    {
        if (!_selectedHeroOverridden || !_originalSelectedHero.HasValue)
            return;

        SetSelectedHero(_originalSelectedHero.Value);
        _selectedHeroOverridden = false;
        _originalSelectedHero = null;
    }

    public static void EnsureOpponentIdentity(
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
                "ReplayOpponentPortrait",
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

    private static async Task<EncounterController?> LoadHeroPortraitAsync(
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

    private static ETier ResolvePortraitTier(PvpBattleManifest manifest)
    {
        if (
            !string.IsNullOrWhiteSpace(manifest?.Participants?.OpponentRank)
            && Enum.TryParse(manifest.Participants.OpponentRank.Trim(), true, out ETier tier)
        )
            return tier;

        return ETier.Bronze;
    }

    private static void SetSelectedHero(EHero hero)
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
