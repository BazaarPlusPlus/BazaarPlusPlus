#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Mobile.Previews;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The Mono host's item tooltip for the Mobile UI's preview switches on a mobile platform
/// (ADR-0025): it reads the shown tooltip each frame, outside a fight. The upgrade preview is
/// <see cref="TouchUpgradePreview"/>'s, which the desktop's tooltip patches read through
/// <c>BppHotkeyService</c>; the enchantments are <see cref="TouchEnchantPreview"/>'s.
/// </summary>
internal sealed class TouchTooltipPreviewHost : ITooltipPreviewHost
{
    private CardTooltipController? _controller;
    private CardTooltipData? _tooltip;
    private Card? _cardInstance;
    private IReadOnlyList<EEnchantmentType> _candidates = Array.Empty<EEnchantmentType>();

    // A new object for each new card: the switches compare it, and nothing allocates per frame.
    private object _card = new();

    public bool TryReadShown(out object card, out object render)
    {
        var shown = Data.TooltipParentComponent?.CardTooltipController;
        var data =
            shown != null && shown.isActiveAndEnabled
                ? shown.CurrentTooltipData as CardTooltipData
                : null;
        if (Data.IsInCombat || data?.CardInstance is not ItemCard)
        {
            card = _card;
            render = _card;
            return false;
        }
        if (
            !ReferenceEquals(data.CardInstance, _cardInstance)
            || !ReferenceEquals(shown, _controller)
        )
        {
            _card = new object();
            _cardInstance = data.CardInstance;
            _controller = shown;
        }
        // The game renders the same card again (an upgrade toggle, a tier pick) as new data.
        _tooltip = data;
        card = _card;
        render = data;
        return true;
    }

    public ScrollRect? DescriptionScroll() => _controller?.MobileSecondaryTooltipScrollRect;

    /// <summary>Whether the game shows a replay, whose cards preview no upgrade (<see cref="TouchUpgradePreview"/>).</summary>
    internal static bool ShowsReplay() => AppState.CurrentState is ReplayState;

    // As on the Android host, BPP never previews the upgrade of a replay's or a recap's card.
    public bool CanToggleUpgrade =>
        _cardInstance is ItemCard item
        && item.CanCardUpgrade()
        && TouchUpgradePreview.Offered
        && Singleton<BoardManager>.Instance?.IsRecapViewOpen != true;

    public bool UpgradeShown => TouchUpgradePreview.Active == true;

    public void ToggleUpgrade()
    {
        if (_tooltip == null || _controller == null || _cardInstance is not ItemCard item)
            return;
        var selected = TouchUpgradePreview.Toggle();
        var card = Data.CardAndSkillLookup?.GetCardController(item);
        if (card != null)
            TooltipPreviewContentRefresh.TryApply(
                card,
                _controller,
                item,
                _controller.CurrentTooltipData as CardTooltipData ?? _tooltip,
                selected ? TooltipPreviewMode.Upgrade : TooltipPreviewMode.Normal,
                selected ? TooltipPreviewMode.Normal : TooltipPreviewMode.Upgrade
            );
        // A refresh hides the legend tray; an unlocked tooltip needs it back.
        if (!_controller.IsLocked)
            _controller._legendRenderer?.OnTooltipShown();
    }

    public bool OffersEnchantments()
    {
        // The main text lists the enchantments under [EnchantPreview] Mode; only a tooltip whose
        // main text lists none offers them behind the switch.
        _candidates =
            _tooltip == null || TouchEnchantPreview.InlineCandidates(_tooltip).Count > 0
                ? Array.Empty<EEnchantmentType>()
                : TouchEnchantPreview.Candidates(_tooltip);
        return _candidates.Count > 0;
    }

    public IReadOnlyList<(string Label, string Text)> EnchantmentEffects() =>
        _tooltip == null
            ? Array.Empty<(string, string)>()
            : TouchEnchantPreview.Build(_tooltip, _candidates);

    public void Release(string reason)
    {
        _controller = null;
        _tooltip = null;
        _cardInstance = null;
        _candidates = Array.Empty<EEnchantmentType>();
        // As on the Android host, the card's next tooltip starts at the settings switch again.
        TouchUpgradePreview.End();
    }
}
