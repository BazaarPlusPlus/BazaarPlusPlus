using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using Il2Cpp;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar;
using Il2CppTheBazaar.SequenceFramework.VisualSequences;
using Il2CppTheBazaar.UI.Components;
using Il2CppTheBazaar.UI.EncounterPicker;
using UnityEngine;
using GamePlayer = Il2CppBazaarGameClient.Domain.Models.Player;

namespace BazaarPlusPlus.Android.CombatReplay.PlaybackUi;

/// <summary>
/// The playback presentation a saved replay repairs around the injected battle, the Android
/// forms of the desktop's <c>HealthBarBinder</c>, <c>PlayerAttributeRepairer</c> and
/// <c>ReplayPresentationRestorer</c>: hide the encounter picker, keep the opponent portrait
/// visible, bind both boards' health bars to the replay's players, fill missing player
/// attributes and the recorded ones, and refresh the clock and board UI to the opening state.
/// The desktop reaches the board UI's private members by reflection; IL2CPP exposes them as
/// interop members. Main thread only; each step is a degradation when it fails.
/// </summary>
internal static class AndroidReplayPlaybackUi
{
    private static readonly HashSet<int> InitializedBoardUi = new();

    /// <summary>The replay's temporary opponent portrait, while one is shown.</summary>
    public static EncounterController? ActiveOpponentPortrait { get; set; }

    public static void ClearBoardUi() => InitializedBoardUi.Clear();

    public static void HideEncounterPickerOverlays()
    {
        foreach (var picker in Resources.FindObjectsOfTypeAll<EncounterPickerMapController>())
        {
            if (picker != null && picker.gameObject != null)
                picker.gameObject.SetActive(false);
        }
        foreach (
            var picker in Resources.FindObjectsOfTypeAll<InjectedEncounterPickerMapController>()
        )
        {
            if (picker != null && picker.gameObject != null)
                picker.gameObject.SetActive(false);
        }
        foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (
                transform != null
                && transform.gameObject != null
                && string.Equals(
                    transform.gameObject.name,
                    "EncounterPicker_Map(Clone)",
                    StringComparison.Ordinal
                )
            )
                transform.gameObject.SetActive(false);
        }
    }

    public static void EnsureOpponentPortraitVisible()
    {
        var portrait = ActiveOpponentPortrait;
        if (portrait != null)
        {
            Data.CurrentEncounterController?.ShowCard(false);
            portrait.gameObject.SetActive(true);
            portrait.ShowCard(true);
            return;
        }
        var encounter = Data.CurrentEncounterController;
        if (encounter == null || encounter.gameObject == null)
            return;
        encounter.gameObject.SetActive(true);
        encounter.ShowCard(true);
    }

    public static async Task PrepareHealthBarsAsync(IReplayPlaybackOutcomeSink outcome)
    {
        var (player, opponent) = ResolveBoardUiControllers();
        if (player != null)
            Bind(player, registerPlayerHealthBar: true, outcome);
        if (opponent != null)
            Bind(opponent, registerPlayerHealthBar: false, outcome);
        await AndroidMainThread.Host.DelayAsync(TimeSpan.FromMilliseconds(150));
        ShowPlayerHealthBar(player, outcome);
        Data.PlayerExperienceBar?.ToggleExperienceBarAndText(false);
        Events.TryShowEmptyOpponentHealthBar.Trigger(null);
    }

    public static void RefillOpponentHealthBar() => Events.TryRefillOpponentHealthBar.Trigger(null);

    public static void EnsureSequencePlayerAttributes(
        CombatSequenceMessages sequence,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        EnsureAttributes(sequence.SpawnMessage?.Data?.Player?.Attributes, outcome);
        EnsureAttributes(sequence.SpawnMessage?.Data?.Opponent?.Attributes, outcome);
        EnsureAttributes(sequence.DespawnMessage?.Data?.Player?.Attributes, outcome);
        EnsureAttributes(sequence.DespawnMessage?.Data?.Opponent?.Attributes, outcome);
    }

    public static void EnsureRunPlayerAttributes(IReplayPlaybackOutcomeSink outcome)
    {
        EnsureAttributes(Data.Run?.Player?.Attributes, outcome);
        EnsureAttributes(Data.Run?.Opponent?.Attributes, outcome);
    }

    public static void RestoreRecordedPlayerAttributes(
        PvpBattleManifest manifest,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var attributes = Data.Run?.Player?.Attributes;
        if (manifest == null || attributes == null)
            return;
        try
        {
            Apply(attributes, EPlayerAttributeType.Level, manifest.Participants.PlayerLevel);
            Apply(attributes, EPlayerAttributeType.Prestige, manifest.Participants.PlayerPrestige);
            Apply(attributes, EPlayerAttributeType.Income, manifest.Participants.PlayerIncome);
            Apply(attributes, EPlayerAttributeType.Gold, manifest.Participants.PlayerGold);
            foreach (var prestige in UnityEngine.Object.FindObjectsOfType<PrestigeBarController>())
                prestige?.ImmediateUpdate();
            foreach (var bank in UnityEngine.Object.FindObjectsOfType<BankToyController>())
                bank?.UpdateUI();
        }
        catch (Exception exception)
        {
            outcome.ReportDegradation(
                ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
                exception
            );
        }
    }

    /// <summary>Rewinds the run's day, hour and levels and the clock and board UI to the opening.</summary>
    public static void RefreshPresentation(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        Step(outcome, "run_state", () => RestoreOpeningRunState(manifest, sequence));
        Step(
            outcome,
            "clock_selection",
            () =>
            {
                var run = Data.Run;
                if (run == null)
                    return;
                foreach (
                    var clock in UnityEngine.Object.FindObjectsOfType<ClockViewController>(true)
                )
                {
                    if (clock == null)
                        continue;
                    Step(
                        outcome,
                        "clock",
                        () =>
                        {
                            clock.Init();
                            clock.UpdateClockImmediate(run);
                        }
                    );
                }
            }
        );
        Step(
            outcome,
            "board_ui_selection",
            () =>
            {
                var run = Data.Run;
                if (run == null)
                    return;
                var (player, opponent) = ResolveBoardUiControllers();
                RefreshBoardUi(player, run.Player, outcome, "player");
                RefreshBoardUi(opponent, run.Opponent, outcome, "opponent");
            }
        );
    }

    public static (
        BoardUIController? Player,
        BoardUIController? Opponent
    ) ResolveBoardUiControllers()
    {
        var controllers = new List<BoardUIController>();
        foreach (var controller in UnityEngine.Object.FindObjectsOfType<BoardUIController>(true))
        {
            if (controller != null && controller.gameObject.scene.rootCount > 0)
                controllers.Add(controller);
        }
        return (
            Select(controllers, ECombatantId.Player, AnchorSide.Player),
            Select(controllers, ECombatantId.Opponent, AnchorSide.Opponent)
        );
    }

    private static BoardUIController? Select(
        IEnumerable<BoardUIController> controllers,
        ECombatantId combatant,
        AnchorSide side
    )
    {
        var anchor = BoardManager.Instance?.GetAnchor(side, AnchorType.Portrait);
        return controllers
            .Where(controller => controller.combatantId == combatant)
            .OrderByDescending(controller => controller.gameObject.activeInHierarchy)
            .ThenByDescending(controller => controller.isActiveAndEnabled)
            .ThenByDescending(controller =>
                controller.HealthBar != null && controller.HealthBar.gameObject.activeInHierarchy
            )
            .ThenBy(controller =>
                anchor == null
                    ? float.MaxValue
                    : Vector3.SqrMagnitude(controller.transform.position - anchor.position)
            )
            .FirstOrDefault();
    }

    private static void Bind(
        BoardUIController controller,
        bool registerPlayerHealthBar,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var player =
            controller.combatantId == ECombatantId.Player ? Data.Run?.Player : Data.Run?.Opponent;
        if (player == null)
            return;
        EnsureAttributes(player.Attributes, outcome);
        if (InitializedBoardUi.Add(controller.GetInstanceID()))
            controller.Init(player);
        controller.SetBattlePlayer(player);
        var healthBar = controller.HealthBar;
        if (healthBar != null && controller.healthBarDividerConfigSO != null)
            healthBar.SetDividerConfig(controller.healthBarDividerConfigSO);
        InitializeHealthBar(controller, player, outcome);
        if (registerPlayerHealthBar && controller.combatantId == ECombatantId.Player)
        {
            try
            {
                Data.RegisterPlayerHealthBar(controller.Cast<IPlayerHealthBar>());
            }
            catch (Exception exception)
            {
                outcome.ReportDegradation(
                    ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
                    exception
                );
            }
        }
    }

    private static void ShowPlayerHealthBar(
        BoardUIController? controller,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var player = Data.Run?.Player;
        if (controller == null)
        {
            Data.PlayerHealthBar?.ShowEmptyPlayerHealthBar();
            return;
        }
        if (player != null)
            EnsureAttributes(player.Attributes, outcome);
        controller.SetBattlePlayer(player);
        InitializeHealthBar(controller, player, outcome);
        controller.ShowEmptyPlayerHealthBar();
        var healthBar = controller.HealthBar;
        if (healthBar == null)
            return;
        healthBar.ToggleBarParent(true);
        healthBar.ToggleStatusNumbers(true);
        healthBar.RefillHealthBar(1f);
        ApplyHealthMax(healthBar, player);
    }

    private static void InitializeHealthBar(
        BoardUIController controller,
        GamePlayer? player,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var healthBar = controller.HealthBar;
        if (healthBar == null || player == null)
            return;
        try
        {
            healthBar.Init(player);
            ApplyHealthMax(healthBar, player);
        }
        catch (Exception exception)
        {
            outcome.ReportDegradation(
                ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
                exception
            );
        }
    }

    private static void ApplyHealthMax(HealthBarController healthBar, GamePlayer? player)
    {
        var attributes = player?.Attributes;
        if (
            attributes == null
            || !attributes.TryGetValue(EPlayerAttributeType.HealthMax, out var max)
        )
            return;
        var value = (uint)Math.Max(0, max);
        healthBar.UpdateMaxHealth(value, value, false);
    }

    private static void RefreshBoardUi(
        BoardUIController? controller,
        GamePlayer? player,
        IReplayPlaybackOutcomeSink outcome,
        string combatant
    )
    {
        if (controller == null || player == null)
            return;
        Step(outcome, $"board_ui_{combatant}", () => controller.UpdateAllUI(player));
        foreach (var plate in controller.GetComponentsInChildren<LevelPlateController>(true))
        {
            if (plate != null)
                Step(outcome, $"level_plate_{combatant}", () => plate.Init(player));
        }
    }

    private static void RestoreOpeningRunState(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence
    )
    {
        var run = Data.Run;
        if (run == null)
            return;
        var opening = sequence.SpawnMessage?.Data?.Run;
        run.Day = ReplaySavedStateRules.ResolvePositiveUInt(opening?.Day ?? 0, manifest.Day);
        run.Hour = ReplaySavedStateRules.ResolvePositiveUInt(opening?.Hour ?? 0, manifest.Hour);
        RestoreOpeningLevel(sequence.SpawnMessage?.Data?.Player?.Attributes, run.Player);
        RestoreOpeningLevel(sequence.SpawnMessage?.Data?.Opponent?.Attributes, run.Opponent);
        Data.AssignHoursInADay();
    }

    private static void RestoreOpeningLevel(
        Il2CppSystem.Collections.Generic.Dictionary<EPlayerAttributeType, int>? opening,
        GamePlayer? live
    )
    {
        var attributes = live?.Attributes;
        if (opening == null || attributes == null)
            return;
        int? openingLevel = opening.TryGetValue(EPlayerAttributeType.Level, out var level)
            ? level
            : null;
        attributes.TryGetValue(EPlayerAttributeType.Level, out var current);
        attributes[EPlayerAttributeType.Level] = ReplaySavedStateRules.ResolveOpeningLevel(
            openingLevel,
            current
        );
    }

    // Every player attribute present, level 1 and the rest 0, and HealthMax from Health.
    private static void EnsureAttributes(
        Il2CppSystem.Collections.Generic.Dictionary<EPlayerAttributeType, int>? attributes,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (attributes == null)
            return;
        try
        {
            foreach (var type in Enum.GetValues<EPlayerAttributeType>())
            {
                if (!attributes.ContainsKey(type))
                    attributes[type] = type == EPlayerAttributeType.Level ? 1 : 0;
            }
            if (attributes.TryGetValue(EPlayerAttributeType.HealthMax, out var max) && max > 0)
                return;
            if (attributes.TryGetValue(EPlayerAttributeType.Health, out var health) && health > 0)
                attributes[EPlayerAttributeType.HealthMax] = health;
        }
        catch (Exception exception)
        {
            outcome.ReportDegradation(
                ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
                exception
            );
        }
    }

    private static void Apply(
        Il2CppSystem.Collections.Generic.Dictionary<EPlayerAttributeType, int> attributes,
        EPlayerAttributeType type,
        int? value
    )
    {
        if (value.HasValue)
            attributes[type] = value.Value;
    }

    private static void Step(IReplayPlaybackOutcomeSink outcome, string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            outcome.ReportDegradation(
                ReplayPlaybackReasonCode.PresentationWarmupFailed,
                new InvalidOperationException($"replay_presentation_restore:{stage}", exception)
            );
        }
    }
}
