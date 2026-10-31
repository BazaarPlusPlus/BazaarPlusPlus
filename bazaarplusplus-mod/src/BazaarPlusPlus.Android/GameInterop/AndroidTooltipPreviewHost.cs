using BazaarPlusPlus.Mobile.Previews;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android host's item tooltip for the Mobile UI's preview switches. <c>CardTooltipRenderPatch</c>
/// hands it each primary tooltip that renders (<see cref="Observe"/>); it reports that tooltip as
/// shown while the game still shows its card, outside a fight and the native settings section.
/// The upgrade preview is <see cref="AndroidUpgradePreview"/>'s, the enchantments the Mobile UI's
/// enchant preview's (<see cref="Patches.AndroidPatchHost.EnchantPreview"/>).
/// </summary>
internal sealed class AndroidTooltipPreviewHost : ITooltipPreviewHost
{
    private CardTooltipController? _controller;
    private CardTooltipData? _tooltip;
    private IReadOnlyList<string> _candidates = [];

    // A new object for each new card and each render: the switches compare them, and nothing
    // allocates per frame.
    private object _card = new();
    private object _render = new();

    /// <summary>A primary tooltip renders its contents.</summary>
    public void Observe(CardTooltipController controller, CardTooltipData tooltip)
    {
        if (_tooltip?.CardInstance.Pointer != tooltip.CardInstance.Pointer)
            _card = new object();
        _render = new object();
        _controller = controller;
        _tooltip = tooltip;
        // The main text lists the enchantments under [EnchantPreview] Mode; only a tooltip whose
        // main text lists none offers them behind the switch.
        var preview = Patches.AndroidPatchHost.EnchantPreview;
        _candidates =
            preview == null || preview.InlineCandidates(tooltip).Count > 0
                ? []
                : preview.Candidates(tooltip);
    }

    public bool TryReadShown(out object card, out object render)
    {
        card = _card;
        render = _render;
        if (_tooltip == null)
            return false;
        var shown = Data.TooltipParentComponent?.CardTooltipController;
        return !Data.IsInCombat
            && !Settings.AndroidNativeSettingsSection.AnyVisible
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
            _controller._legendRenderer?.OnTooltipShown();
    }

    public bool OffersEnchantments() => _candidates.Count > 0;

    public IReadOnlyList<(string Label, string Text)> EnchantmentEffects() =>
        _tooltip == null || Patches.AndroidPatchHost.EnchantPreview is not { } preview
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
