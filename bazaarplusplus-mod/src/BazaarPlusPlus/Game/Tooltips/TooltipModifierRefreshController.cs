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
            // TooltipPreviewModePolicy.Resolve is a pure function of these inputs, so the
            // resolved mode cannot change unless one of them changes. Skip re-resolving
            // (and the downstream refresh check) on frames where the inputs are identical.
            var inputs = ReadResolveInputs();
            if (_hasResolvedInputs && inputs.Equals(_lastInputs))
                return;

            _hasResolvedInputs = true;
            _lastInputs = inputs;

            var mode = TooltipPreviewModePolicy.Resolve(
                _config,
                _encounterState,
                inputs.HoldUpgrade,
                inputs.HoldEnchant
            );
            if (mode == _lastMode)
                return;

            _lastMode = mode;
            TryRefreshCurrentItemTooltip(_config, _encounterState, ToLogMode(_lastMode));
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
        var holdUpgrade = BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview);
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
        IBppConfig? config,
        IEncounterStateProbe? encounterState,
        TooltipPreviewRefreshMode mode
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

        var refreshedTooltipData = CardTooltipDataFactory.Create(
            target.Card,
            target.TooltipData,
            mode
        );

        tooltipParent.HideCardTooltipController();
        tooltipParent.ShowCardTooltipController(
            target.Controller.transform,
            target.Controller.TooltipOffset,
            refreshedTooltipData
        );
        UpgradeTooltipScheduler.TryScheduleUpgradeTooltip(
            target.Controller,
            config,
            encounterState,
            refreshedTooltipData
        );
    }

    private bool TryRefreshHoveredPreviewTooltip(
        TooltipParentComponent tooltipParent,
        TooltipPreviewRefreshMode mode
    )
    {
        if (_nativeCardPreviewHost == null)
            return false;

        var result = _nativeCardPreviewHost.RefreshHoveredTooltip(
            new NativeTooltipRefreshRequest(
                tooltipParent,
                mode switch
                {
                    TooltipPreviewRefreshMode.Enchant => NativeTooltipRefreshMode.Enchant,
                    TooltipPreviewRefreshMode.Upgrade => NativeTooltipRefreshMode.Upgrade,
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

            if (!controller.IsCursorOverCard && !controller.IsHovering)
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
