#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// Touch actions for the native item tooltip on a mobile platform (ADR-0024), as the Android
/// host's <c>AndroidGameplayPreviews</c>: an Upgrade preview toggle and an Enchantments toggle
/// that lists each other enchantment's effect, drawn into the native description column so they
/// scroll with it. They replace the desktop's Shift and Ctrl hotkeys and the enchant preview
/// mode, which a touch screen cannot reach.
/// </summary>
internal sealed class TouchGameplayPreviews : MonoBehaviour
{
    private CardTooltipController? _controller;
    private CardTooltipData? _tooltip;
    private Card? _card;
    private IReadOnlyList<EEnchantmentType> _candidates = Array.Empty<EEnchantmentType>();
    private IReadOnlyList<(string Label, string Text)>? _effects;
    private GameObject? _root;
    private TouchNativeSkin? _skin;
    private bool _expanded;
    private bool _dirty;
    private string? _screen;

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.gameplay_preview.tick_failed"),
                exception
            );
            Clear("tick_failed");
        }
    }

    private void Tick()
    {
        var shown = Data.TooltipParentComponent?.CardTooltipController;
        var data =
            shown != null && shown.isActiveAndEnabled
                ? shown.CurrentTooltipData as CardTooltipData
                : null;
        if (Data.IsInCombat || data?.CardInstance is not ItemCard)
        {
            if (_tooltip != null)
                Clear("tooltip_closed");
            return;
        }
        if (!ReferenceEquals(data.CardInstance, _card) || !ReferenceEquals(shown, _controller))
            Observe(shown!, data);
        else if (!ReferenceEquals(data, _tooltip))
        {
            // The game re-rendered the same card (an upgrade toggle, a tier pick): draw again.
            _tooltip = data;
            _dirty = true;
        }
        var screen = TouchScreen.Key();
        if (screen != _screen)
        {
            _screen = screen;
            _dirty = true;
        }
        if (_dirty && TouchFonts.Body() != null)
            Render();
    }

    private void Observe(CardTooltipController controller, CardTooltipData tooltip)
    {
        _expanded = false;
        _effects = null;
        _controller = controller;
        _tooltip = tooltip;
        _card = tooltip.CardInstance;
        _candidates = TouchEnchantPreview.Candidates(tooltip);
        _dirty = true;
    }

    private void Clear(string reason)
    {
        DestroyView();
        _controller = null;
        _tooltip = null;
        _card = null;
        _effects = null;
        _expanded = false;
        _candidates = Array.Empty<EEnchantmentType>();
        Log(reason);
    }

    private bool CanToggleUpgrade =>
        _card is ItemCard item && item.CanCardUpgrade() && TouchUpgradePreview.Active != null;

    private void ToggleUpgrade()
    {
        if (_tooltip == null || _controller == null || _card is not ItemCard item)
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
        _dirty = true;
        Log("upgrade_toggled");
    }

    private void ToggleEnchantments()
    {
        if (_tooltip == null)
            return;
        _expanded = !_expanded;
        if (_expanded)
            _effects ??= TouchEnchantPreview.Build(_tooltip, _candidates);
        _dirty = true;
        Log("enchantments_toggled");
    }

    private void Render()
    {
        var scroll = _controller?.MobileSecondaryTooltipScrollRect;
        var parent = scroll?.content;
        if (parent == null || parent.rect.width <= 0)
            return;
        _dirty = false;
        DestroyView();
        var upgrade = CanToggleUpgrade;
        if (!upgrade && _candidates.Count == 0)
            return;

        // Join the native description column so the toggles and effects move and scroll with
        // the card, including the game's transition between inspected cards.
        var canvas = parent.GetComponentInParent<Canvas>(true).rootCanvas;
        var eventCamera =
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4];
        parent.GetWorldCorners(corners);
        var left = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
        var right = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[3]);
        var pixelsPerUnit = Math.Max(0.01f, Vector2.Distance(left, right) / parent.rect.width);
        var chrome = TouchChrome.For(
            TouchMetrics.From(
                Screen.width,
                Screen.height,
                Screen.dpi,
                Screen.height / pixelsPerUnit,
                0,
                0,
                Screen.width,
                Screen.height
            )
        );
        var root = TouchScreen.Child(
            "BPP.GameplayPreviews",
            parent,
            new(0, 0, parent.rect.width, 0)
        );
        _root = root.gameObject;
        var area = TouchScreen.Child(
            "Content",
            root,
            new(chrome.Padding, chrome.Gap, parent.rect.width - 2 * chrome.Padding, 0)
        );
        if (_skin == null || _skin.NeedsRefresh)
            _skin = new TouchNativeSkin();
        var flow = new TouchChipFlow(area, TouchFonts.Body()!, chrome, 1, chrome.Target, _skin);
        if (upgrade)
            flow.Toggle(
                L.Resolve(new LocalizedTextSet("Upgrade preview", "升级预览", "升級預覽")),
                TouchUpgradePreview.Active == true,
                _ => TouchUiActions.Run("upgrade_preview", ToggleUpgrade)
            );
        if (_candidates.Count > 0)
            flow.Toggle(
                L.Resolve(new LocalizedTextSet("Enchantments", "附魔效果", "附魔效果")),
                _expanded,
                _ => TouchUiActions.Run("enchant_preview", ToggleEnchantments)
            );
        if (_expanded)
        {
            if (_effects is { Count: > 0 })
                flow.Definitions(_effects.ToList());
            else
                flow.Note(
                    L.Resolve(
                        new LocalizedTextSet("Effect unavailable", "效果暂不可用", "效果暫不可用")
                    ),
                    TouchUiKit.MutedTextColor
                );
        }
        var height = flow.Height + 2 * chrome.Gap;
        area.sizeDelta = new Vector2(area.rect.width, flow.Height);
        root.sizeDelta = new Vector2(parent.rect.width, height);
        var layout = _root.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleWidth = 1;
        LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    private void DestroyView()
    {
        if (_root == null)
            return;
        _root.SetActive(false);
        Destroy(_root);
        _root = null;
    }

    private void OnDestroy() => DestroyView();

    private void Log(string action) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.gameplay_preview.changed"),
            ("action", action),
            ("upgrade", TouchUpgradePreview.Active == true),
            ("expanded", _expanded),
            ("effects", _effects?.Count ?? 0)
        );
}
