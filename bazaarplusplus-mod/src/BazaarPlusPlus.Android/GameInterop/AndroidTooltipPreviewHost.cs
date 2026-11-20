using BazaarPlusPlus.Mobile.Previews;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;
using Il2CppTheBazaar.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

internal sealed class AndroidTooltipPreviewHost : ITooltipPreviewHost
{
    private CardTooltipController? _controller;
    private CardTooltipData? _tooltip;
    private IReadOnlyList<string> _candidates = [];

    // The switches compare these objects by identity, and each must stay stable until the card or
    // render changes.
    private object _card = new();
    private object _render = new();

    public void Observe(CardTooltipController controller, CardTooltipData tooltip)
    {
        if (_tooltip?.CardInstance.Pointer != tooltip.CardInstance.Pointer)
            _card = new object();
        _render = new object();
        _controller = controller;
        _tooltip = tooltip;
        _candidates =
            Patches.AndroidPatchHost.Features?.EnchantPreview?.SwitchCandidates(tooltip) ?? [];
    }

    public bool TryReadShown(out object card, out object render)
    {
        card = _card;
        render = _render;
        if (_tooltip == null)
            return false;
        var shown = Data.TooltipParentComponent?.CardTooltipController;
        return !Settings.AndroidNativeSettingsSection.AnyVisible
            && shown != null
            && shown.Pointer == _controller?.Pointer
            && shown.CurrentCard?.Pointer == _tooltip.CardInstance.Pointer;
    }

    public ScrollRect? DescriptionScroll() => _controller?.MobileSecondaryTooltipScrollRect;

    public bool CanToggleUpgrade => AndroidUpgradePreview.CanToggle;

    public bool UpgradeShown => AndroidUpgradePreview.Selected;

    public void ToggleUpgrade()
    {
        if (_tooltip == null || _controller == null)
            return;
        AndroidUpgradePreview.Toggle();
        var card = Data.CardAndSkillLookup?.GetCardController(_tooltip.CardInstance);
        if (card != null)
            _tooltip.CanFuse = card.CanFuse();
        _controller.ApplyPreviewTooltip(_tooltip);
        // ApplyPreviewTooltip restores legends only for locked previews. A touch tooltip also
        // needs its normal tray restored after RenderLegend hides it during refresh.
        if (!_controller.isLocked)
        {
            _controller._legendRenderer?.OnTooltipShown();
            // Only locked previews rebuild their layout. A stale touch rect makes the game treat
            // taps inside the tooltip as outside.
            Canvas.ForceUpdateCanvases();
            TempoUIUtility.ForceRebuildRecursive(_controller.PositioningRectTransform);
            _controller.KeepTooltipWithinBounds();
        }
    }

    public bool OffersEnchantments() => _candidates.Count > 0;

    public IReadOnlyList<(string Label, string Text)> EnchantmentEffects() =>
        _tooltip == null || Patches.AndroidPatchHost.Features?.EnchantPreview is not { } preview
            ? []
            : preview.Build(_tooltip, _candidates);

    public void Release(string reason)
    {
        _controller = null;
        _tooltip = null;
        _candidates = [];
        AndroidUpgradePreview.Release(reason);
    }
}
