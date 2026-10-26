#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class TooltipModifierRefreshController : MonoBehaviour
{
    // Upper bound on how long a reused pedestal kind can miss an in-place SelectionSet edit
    // that keeps the same list instance and Count.
    private const int PedestalKindRefreshFrames = 30;

    private TooltipPreviewMode _lastMode;
    private BppSettings? _config;
    private IEncounterStateProbe? _encounterState;
    private INativeCardPreviewHost? _nativeCardPreviewHost;
    private bool _hasResolvedInputs;
    private ResolveInputs _lastInputs;
    private bool _hasPedestalKind;
    private PedestalProbeKey _pedestalKey;
    private int _pedestalKindReadFrame;
    private ChoiceScreenPedestalKind _pedestalKind;

    internal void Initialize(
        BppSettings config,
        IEncounterStateProbe encounterState,
        INativeCardPreviewHost nativeCardPreviewHost
    )
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
        _nativeCardPreviewHost =
            nativeCardPreviewHost ?? throw new ArgumentNullException(nameof(nativeCardPreviewHost));
    }

    private void Update()
    {
        try
        {
            if (Singleton<BoardManager>.Instance?.IsRecapViewOpen == true)
            {
                UpgradePreviewCardLatch.ReleaseStale(previewActive: false);
                _hasResolvedInputs = false;
                return;
            }

            // TooltipPreviewModePolicy.Resolve is a pure function of these inputs, so the
            // resolved mode cannot change unless one of them changes. Skip re-resolving
            // (and the downstream refresh check) on frames where the inputs are identical.
            // The latch sweep still runs on those frames: hover moving off a card changes
            // no resolve input, and the native hide path skips its upgrade-preview exit
            // while a card-to-card tooltip transition is active.
            var inputs = ReadResolveInputs();
            if (_hasResolvedInputs && inputs.Equals(_lastInputs))
            {
                UpgradePreviewCardLatch.ReleaseStale(_lastMode == TooltipPreviewMode.Upgrade);
                return;
            }

            _hasResolvedInputs = true;
            _lastInputs = inputs;

            var mode = TooltipPreviewModePolicy.Resolve(
                _config,
                _encounterState,
                inputs.HoldUpgrade,
                inputs.HoldEnchant
            );
            UpgradePreviewCardLatch.ReleaseStale(mode == TooltipPreviewMode.Upgrade);
            if (mode == _lastMode)
                return;

            var previousMode = _lastMode;
            _lastMode = mode;
            TryRefreshCurrentItemTooltip(mode, previousMode);
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Tooltips,
                    "tooltips.preview_refresh.degraded",
                    storm: ["reason_code"]
                ),
                ex,
                ("reason_code", TooltipLogReasonCode.PreviewRefreshException),
                ("mode", _lastMode)
            );
        }
    }

    private ResolveInputs ReadResolveInputs()
    {
        var holdUpgrade = BppHotkeyService.IsActive(BppHotkeyActionId.HoldUpgradePreview);
        var holdEnchant = BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview);
        var enchantMode = _config?.EnchantPreviewModeConfig.Value;
        var pedestalKind = ChoiceScreenPedestalKind.None;
        if (
            !holdUpgrade
            && !holdEnchant
            && (enchantMode ?? BppSettings.DefaultEnchantPreviewMode)
                == PreviewVisibilityMode.AutoOnPedestalChoice
        )
        {
            pedestalKind = ReadPedestalKind();
        }

        return new ResolveInputs(holdUpgrade, holdEnchant, enchantMode, pedestalKind);
    }

    // The encounter probe rebuilds the selection ids and the pedestal snapshot on every frame it
    // is read. The kind can only change with the app state, the selection set, or the encounter,
    // so reuse it while those are unchanged, rereading every PedestalKindRefreshFrames frames.
    private ChoiceScreenPedestalKind ReadPedestalKind()
    {
        var runState = Data.CurrentState;
        var selection = runState?.SelectionSet;
        var key = new PedestalProbeKey(
            AppState.CurrentState,
            selection,
            selection?.Count ?? 0,
            runState?.CurrentEncounterId
        );
        var frame = Time.frameCount;
        if (
            _hasPedestalKind
            && key.Equals(_pedestalKey)
            && frame - _pedestalKindReadFrame < PedestalKindRefreshFrames
        )
            return _pedestalKind;

        _pedestalKind =
            TooltipEncounterProbeReader.ReadChoice(_encounterState)?.Kind
            ?? ChoiceScreenPedestalKind.None;
        _pedestalKey = key;
        _pedestalKindReadFrame = frame;
        _hasPedestalKind = true;
        return _pedestalKind;
    }

    private readonly record struct PedestalProbeKey(
        object? AppState,
        object? SelectionSet,
        int SelectionCount,
        string? CurrentEncounterId
    )
    {
        // Game states and the selection list are compared by identity, not by any Equals override.
        public bool Equals(PedestalProbeKey other) =>
            ReferenceEquals(AppState, other.AppState)
            && ReferenceEquals(SelectionSet, other.SelectionSet)
            && SelectionCount == other.SelectionCount
            && string.Equals(
                CurrentEncounterId,
                other.CurrentEncounterId,
                StringComparison.Ordinal
            );

        public override int GetHashCode() => SelectionCount;
    }

    private readonly record struct ResolveInputs(
        bool HoldUpgrade,
        bool HoldEnchant,
        PreviewVisibilityMode? EnchantMode,
        ChoiceScreenPedestalKind PedestalKind
    );

    private void TryRefreshCurrentItemTooltip(
        TooltipPreviewMode mode,
        TooltipPreviewMode previousMode
    )
    {
        var tooltipParent = Data.TooltipParentComponent;
        if (tooltipParent == null)
            return;

        if (tooltipParent.HasAnyLockedTooltipControllers())
            return;

        if (TryRefreshHoveredPreviewTooltip(tooltipParent, mode))
            return;

        if (!TryResolveRefreshTarget(tooltipParent, out var target))
            return;

        // A primary tooltip can remain addressable briefly after native hover-out. Toggle mode
        // stays active across that window, so refreshing it would re-enter upgrade preview after
        // the native hide path already cleared the card and leave the fusion visual latched.
        if (!target.Controller.IsCursorOverCard)
            return;

        var tooltipController = tooltipParent.GetCardTooltipController(target.Card);
        if (tooltipController == null)
            return;

        TooltipPreviewContentRefresh.TryApply(
            target.Controller,
            tooltipController,
            target.Card,
            target.TooltipData,
            mode,
            previousMode
        );
    }

    private bool TryRefreshHoveredPreviewTooltip(
        TooltipParentComponent tooltipParent,
        TooltipPreviewMode mode
    )
    {
        if (_nativeCardPreviewHost == null)
            return false;

        var result = _nativeCardPreviewHost.RefreshHoveredTooltip(
            new NativeTooltipRefreshRequest(
                tooltipParent,
                mode switch
                {
                    TooltipPreviewMode.Enchant => NativeTooltipRefreshMode.Enchant,
                    TooltipPreviewMode.Upgrade => NativeTooltipRefreshMode.Upgrade,
                    _ => NativeTooltipRefreshMode.Normal,
                }
            )
        );
        if (result.Status == NativeTooltipRefreshStatus.Refreshed && result.Card != null)
        {
            TooltipPreviewTargetResolver.Report(
                TooltipPreviewTargetOutcome.Resolved,
                TooltipLogReasonCode.PreviewCardMatched,
                result.Card
            );
            return true;
        }

        return result.Status
            is NativeTooltipRefreshStatus.NoChange
                or NativeTooltipRefreshStatus.Failed;
    }

    private static bool TryResolveRefreshTarget(
        TooltipParentComponent tooltipParent,
        out TooltipPreviewTargetResolver.TooltipRefreshTarget target
    )
    {
        if (
            TooltipPreviewTargetResolver.TryResolveCurrentPrimaryItemTooltip(
                tooltipParent,
                out target
            )
        )
            return true;

        var lookup = Data.CardAndSkillLookup;
        if (lookup == null)
        {
            target = default;
            return false;
        }

        foreach (var controller in lookup.CardControllerDictionary.Values)
        {
            if (controller?.CardData is not ItemCard itemCard)
                continue;

            if (!controller.IsCursorOverCard)
                continue;

            if (tooltipParent.GetCardTooltipController(itemCard) == null)
                continue;

            if (controller.GetTooltipData() is not CardTooltipData tooltipData)
                continue;

            target = new TooltipPreviewTargetResolver.TooltipRefreshTarget(
                controller,
                itemCard,
                tooltipData
            );
            return true;
        }

        target = default;
        return false;
    }
}
