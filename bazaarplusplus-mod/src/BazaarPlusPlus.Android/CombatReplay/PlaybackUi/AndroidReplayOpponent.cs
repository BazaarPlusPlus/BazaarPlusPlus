using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2Cpp;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppBazaarGameShared.Infra.Messages.GameSimEvents;
using Il2CppBazaarGameShared.TempoNet.Enums;
using Il2CppBazaarGameShared.TempoNet.Models;
using Il2CppTheBazaar;
using Il2CppTheBazaar.AppFramework;

namespace BazaarPlusPlus.Android.CombatReplay.PlaybackUi;

/// <summary>
/// The two players a saved replay presents, the Android form of the desktop's
/// <c>OpponentPortraitController</c>: the opponent's identity from the manifest when the spawn
/// carries none, a temporary opponent portrait loaded through the game's
/// <c>BoardBuilder.LoadHeroPortraitAsync</c>, and the selected hero switched to the recorder's
/// for the replay and restored after. In a Ghost payload the recorder is the challenger, on the
/// Player side (ADR-0002). Main thread only.
/// </summary>
internal sealed class AndroidReplayOpponent
{
    private EncounterController? _portrait;
    private EHero? _originalSelectedHero;
    private bool _selectedHeroOverridden;

    public async Task EnsureTemporaryPortraitAsync(
        PvpBattleManifest manifest,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (_portrait != null)
        {
            _portrait.gameObject.SetActive(true);
            _portrait.ShowCard(true);
            AndroidReplayPlaybackUi.ActiveOpponentPortrait = _portrait;
            return;
        }
        var boardManager = BoardManager.Instance;
        if (boardManager == null)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.OpponentPortraitUnavailable);
            return;
        }
        var opponent = Data.SimPvpOpponent;
        EHero? hero = opponent?.Hero;
        if (hero is null or EHero.Common)
        {
            if (!TryParseHero(manifest.Participants.OpponentHero, out var parsed))
            {
                outcome.ReportDegradation(ReplayPlaybackReasonCode.OpponentPortraitUnavailable);
                return;
            }
            hero = parsed;
        }
        var collection =
            Services.Get<CollectionManager>()
            ?? throw new InvalidOperationException("CollectionManager is unavailable.");
        var loadout = opponent?.PlayerLoadout ?? EmptyLoadout(manifest);
        var skin =
            await Il2CppTasks.AwaitAsync(
                collection.GetEquippedHeroSkin(hero.Value, loadout),
                "CollectionManager.GetEquippedHeroSkin(loadout)"
            )
            ?? await Il2CppTasks.AwaitAsync(
                collection.GetEquippedHeroSkin(hero.Value),
                "CollectionManager.GetEquippedHeroSkin"
            );
        if (skin == null)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.OpponentPortraitUnavailable);
            return;
        }
        var anchor = boardManager.GetAnchor(AnchorSide.Opponent, AnchorType.Portrait);
        var portrait = await Il2CppTasks.AwaitAsync(
            BoardBuilder.LoadHeroPortraitAsync(skin, PortraitTier(manifest), anchor, false),
            "BoardBuilder.LoadHeroPortraitAsync"
        );
        if (portrait == null)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.OpponentPortraitUnavailable);
            return;
        }
        Data.CurrentEncounterController?.ShowCard(false);
        portrait.gameObject.name = "ReplayOpponentPortrait";
        portrait.gameObject.SetActive(true);
        portrait.ShowCard(true);
        _portrait = portrait;
        AndroidReplayPlaybackUi.ActiveOpponentPortrait = portrait;
    }

    public void Cleanup(string? battleId)
    {
        if (_portrait != null)
        {
            try
            {
                UnityEngine.Object.Destroy(_portrait.gameObject);
            }
            catch (Exception exception)
            {
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.playback.cleanup_observed"
                    ),
                    exception,
                    () =>
                        [
                            ("stage", "opponent_portrait"),
                            ("removed_count", 0),
                            ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                        ]
                );
            }
            _portrait = null;
            AndroidReplayPlaybackUi.ActiveOpponentPortrait = null;
        }
        var encounter = Data.CurrentEncounterController;
        if (encounter != null)
        {
            encounter.gameObject.SetActive(true);
            encounter.ShowCard(true);
        }
    }

    public void ApplySelectedHeroOverride(PvpBattleManifest manifest)
    {
        if (manifest?.Participants == null)
            return;
        if (!TryParseHero(manifest.Participants.PlayerHero, out var replayHero))
            return;
        _originalSelectedHero = Data.SelectedHero;
        if (_originalSelectedHero == replayHero)
        {
            _selectedHeroOverridden = false;
            _originalSelectedHero = null;
            return;
        }
        ClientCache.RunConfig.SetSelectedHero(replayHero);
        _selectedHeroOverridden = true;
    }

    public void RestoreSelectedHeroOverride()
    {
        if (!_selectedHeroOverridden || _originalSelectedHero is not { } original)
            return;
        ClientCache.RunConfig.SetSelectedHero(original);
        _selectedHeroOverridden = false;
        _originalSelectedHero = null;
    }

    public static void EnsureOpponentIdentity(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (manifest?.Participants == null)
            return;
        if (spawnMessage?.Data?.CurrentState?.PvpOpponent != null)
            return;
        if (!TryParseHero(manifest.Participants.OpponentHero, out var opponentHero))
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.OpponentIdentityUnavailable);
            return;
        }
        var participants = manifest.Participants;
        Data.SimPvpOpponent = new SimPvpOpponent(
            participants.OpponentName,
            null,
            null,
            TryParseRank(participants.OpponentRank) is { } rank
                ? new Il2CppSystem.Nullable<ERank>(rank)
                : null!,
            participants.OpponentRating ?? 0,
            null!,
            null!,
            null!,
            participants.OpponentLevel is { } level ? new Il2CppSystem.Nullable<int>(level) : null!,
            new Il2CppSystem.Nullable<EHero>(opponentHero),
            EmptyLoadout(manifest),
            null
        );
    }

    // A stored hero id (BPP's canonical form, The Dragons included) back to the game's hero.
    private static bool TryParseHero(string? heroId, out EHero hero)
    {
        hero = default;
        if (string.IsNullOrWhiteSpace(heroId))
            return false;
        var trimmed = heroId.Trim();
        if (TheDragonsHeroId.IsAlias(trimmed))
            return AndroidCardFacets.Hero.TryToSource(CardHero.TheDragons, out hero);
        return Enum.TryParse(trimmed, true, out hero);
    }

    private static ETier PortraitTier(PvpBattleManifest manifest) =>
        !string.IsNullOrWhiteSpace(manifest?.Participants?.OpponentRank)
        && Enum.TryParse(manifest.Participants.OpponentRank.Trim(), true, out ETier tier)
            ? tier
            : ETier.Bronze;

    private static ERank? TryParseRank(string? rank) =>
        !string.IsNullOrWhiteSpace(rank) && Enum.TryParse(rank.Trim(), true, out ERank parsed)
            ? parsed
            : null;

    private static BazaarCollectionLoadout EmptyLoadout(PvpBattleManifest manifest) =>
        new()
        {
            accountId = manifest.Participants.OpponentAccountId ?? string.Empty,
            heroSkinIds = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(0),
            cardSkinIds = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(0),
        };
}
