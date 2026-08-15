#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.Tooltips;

internal sealed class TooltipModifierRefreshController : MonoBehaviour
{
    private TooltipPreviewMode _lastMode;
    private IBppConfig? _config;
    private IEncounterStateProbe? _encounterState;
    private INativeCardPreviewHost? _nativeCardPreviewHost;
    private bool _hasResolvedInputs;
    private ResolveInputs _lastInputs;

    internal void Initialize(
        IBppConfig config,
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
                TooltipLogEvents.PreviewRefreshDegraded,
                ex,
                TooltipLogEvents.PreviewRefreshReasonCode.Bind(
                    TooltipLogReasonCode.PreviewRefreshException
                ),
                TooltipLogEvents.PreviewRefreshMode.Bind(ToLogMode(_lastMode))
            );
        }
    }

    private ResolveInputs ReadResolveInputs()
    {
        var holdUpgrade = BppHotkeyService.IsActive(BppHotkeyActionId.HoldUpgradePreview);
        var holdEnchant = BppHotkeyService.IsHeld(BppHotkeyActionId.HoldEnchantPreview);
        var enchantMode = _config?.EnchantPreviewModeConfig?.Value;
        var pedestalKind = ChoiceScreenPedestalKind.None;
        if (
            !holdUpgrade
            && !holdEnchant
            && (enchantMode ?? BppConfig.DefaultEnchantPreviewMode)
                == PreviewVisibilityMode.AutoOnPedestalChoice
        )
        {
            pedestalKind =
                TooltipEncounterProbeReader.ReadChoice(_encounterState)?.Kind
                ?? ChoiceScreenPedestalKind.None;
        }

        return new ResolveInputs(holdUpgrade, holdEnchant, enchantMode, pedestalKind);
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

    private static TooltipPreviewRefreshMode ToLogMode(TooltipPreviewMode mode) =>
        mode switch
        {
            TooltipPreviewMode.Enchant => TooltipPreviewRefreshMode.Enchant,
            TooltipPreviewMode.Upgrade => TooltipPreviewRefreshMode.Upgrade,
            _ => TooltipPreviewRefreshMode.Normal,
        };
}
