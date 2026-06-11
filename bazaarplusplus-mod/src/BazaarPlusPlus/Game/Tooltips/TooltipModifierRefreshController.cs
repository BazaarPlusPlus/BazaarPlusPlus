#nullable enable
using System;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.Input;
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
    private bool _hasResolvedInputs;
    private ResolveInputs _lastInputs;

    internal void Initialize(IBppConfig config, IEncounterStateProbe encounterState)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _encounterState = encounterState ?? throw new ArgumentNullException(nameof(encounterState));
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
            TryRefreshCurrentItemTooltip(_config, _encounterState);
        }
        catch (Exception ex)
        {
            BppLog.Error("TooltipPreview", "Tooltip modifier update failed", ex);
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
                _encounterState?.GetChoicePedestal().Kind ?? ChoiceScreenPedestalKind.None;
        }

        return new ResolveInputs(holdUpgrade, holdEnchant, enchantMode, pedestalKind);
    }

    private readonly record struct ResolveInputs(
        bool HoldUpgrade,
        bool HoldEnchant,
        PreviewVisibilityMode? EnchantMode,
        ChoiceScreenPedestalKind PedestalKind
    );

    private static void TryRefreshCurrentItemTooltip(
        IBppConfig? config,
        IEncounterStateProbe? encounterState
    )
    {
        var tooltipParent = Data.TooltipParentComponent;
        if (tooltipParent == null)
            return;

        if (tooltipParent.HasAnyLockedTooltipControllers())
            return;

        if (!TryResolveRefreshTarget(tooltipParent, out var target))
            return;

        var refreshedTooltipData = CardTooltipDataFactory.Create(target.Card, target.TooltipData);

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
}
