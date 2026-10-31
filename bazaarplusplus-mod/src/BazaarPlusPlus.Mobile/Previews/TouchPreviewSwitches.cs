using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Previews;

/// <summary>
/// The touch switches in the game's item tooltip, which replace the desktop's Shift and Ctrl hold
/// keys: Upgrade preview, and Enchantments for an item whose main text lists none, with each other
/// enchantment's effect expanded below it. They join the tooltip's description scroll, so they
/// move and scroll with the card, and hide under an open panel. The HUD ticks them
/// (<see cref="Hud.TouchHud"/>); the host reads and writes the tooltip
/// (<see cref="ITooltipPreviewHost"/>). Owns no rented cards. Main thread.
/// </summary>
internal sealed class TouchPreviewSwitches : IDisposable
{
    private static readonly LocalizedTextSet UpgradeLabel = new(
        "Upgrade preview",
        "升级预览",
        "升級預覽"
    );
    private static readonly LocalizedTextSet EnchantmentsLabel = new(
        "Enchantments",
        "附魔效果",
        "附魔效果"
    );
    private static readonly LocalizedTextSet EffectUnavailable = new(
        "Effect unavailable",
        "效果暂不可用",
        "效果暫不可用"
    );

    private readonly ITooltipPreviewHost _host;
    private object? _card;
    private object? _render;
    private bool _offersEnchantments;
    private IReadOnlyList<(string Label, string Text)>? _effects;
    private GameObject? _root;
    private RectTransform? _actions;
    private ScrollRect? _nativeScroll;
    private Camera? _eventCamera;

    // Kept across renders, as the panels keep theirs; rebuilt only once its art is gone.
    private NativeSkin? _skin;
    private bool _expanded;
    private bool _dirty;
    private string? _screen;

    public TouchPreviewSwitches(ITooltipPreviewHost host) =>
        _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>
    /// Whether the focused app's pointer is inside the visible switches or expanded effects, so a
    /// host can keep the tooltip its own input would dismiss after a switch's tap.
    /// </summary>
    public bool ContainsPointer()
    {
        var pointer = UnityEngine.InputSystem.Pointer.current;
        if (_root == null || !_root.activeInHierarchy || pointer == null || !Application.isFocused)
            return false;
        var position = pointer.position.ReadValue();
        return _actions != null
            && RectTransformUtility.RectangleContainsScreenPoint(_actions, position, _eventCamera)
            && (
                _nativeScroll?.viewport == null
                || RectTransformUtility.RectangleContainsScreenPoint(
                    _nativeScroll.viewport,
                    position,
                    _eventCamera
                )
            );
    }

    /// <summary>Every frame, from the HUD's tick. A failure clears the switches, so no view leaks.</summary>
    public void Tick()
    {
        try
        {
            Follow();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.preview.tick_failed"),
                exception
            );
            Clear("tick_failed");
        }
    }

    /// <summary>
    /// The back key: collapses the open effects and returns true, or clears the switches and
    /// returns false, so the tooltip takes the key.
    /// </summary>
    public bool Collapse(string reason)
    {
        if (!_expanded)
        {
            Clear(reason);
            return false;
        }
        _expanded = false;
        _dirty = true;
        Log(reason);
        return true;
    }

    /// <summary>The tooltip closed or the scene changed: the switches go, and the host lets go of the tooltip.</summary>
    public void Clear(string reason)
    {
        var held = _card != null;
        DestroyView();
        _card = null;
        _render = null;
        _effects = null;
        _expanded = false;
        _offersEnchantments = false;
        _host.Release(reason);
        if (held)
            Log(reason);
    }

    public void Dispose() => Clear("stopped");

    private void Follow()
    {
        if (
            MobilePlatform.Port.OpenPanelId != null
            || !_host.TryReadShown(out var card, out var render)
        )
        {
            if (_card != null)
                Clear("tooltip_closed");
            return;
        }
        if (!Equals(card, _card))
        {
            _card = card;
            _render = null;
            _expanded = false;
            _effects = null;
        }
        if (!Equals(render, _render))
        {
            // The main text lists the enchantments under [EnchantPreview] Mode; only a tooltip
            // whose main text lists none offers them here, behind the switch.
            _render = render;
            _offersEnchantments = _host.OffersEnchantments();
            _dirty = true;
        }
        var screen = TouchScreen.Key();
        if (screen != _screen)
        {
            _screen = screen;
            _dirty = true;
        }
        if (_dirty && MobilePlatform.Port.GameFont() != null)
            Render();
    }

    private void ToggleUpgrade()
    {
        if (_card == null)
            return;
        _host.ToggleUpgrade();
        _dirty = true;
        Log("upgrade_toggled");
    }

    private void ToggleEnchantments()
    {
        if (_card == null)
            return;
        _expanded = !_expanded;
        if (_expanded)
            _effects ??= _host.EnchantmentEffects();
        _dirty = true;
        Log("enchantments_toggled");
    }

    private void Render()
    {
        var scroll = _host.DescriptionScroll();
        var parent = scroll?.content;
        if (scroll == null || parent == null || parent.rect.width <= 0)
            return;
        _dirty = false;
        DestroyView();
        var upgrade = _host.CanToggleUpgrade;
        if (!upgrade && !_offersEnchantments)
            return;

        // Join the native description column so the switches and effects move and scroll with
        // the card, including the game's transition between inspected cards.
        var platform = MobilePlatform.Port;
        _nativeScroll = scroll;
        var canvas = parent.GetComponentInParent<Canvas>(true).rootCanvas;
        _eventCamera =
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var pixelsPerUnit = Math.Max(
            0.01f,
            platform.ScreenBounds(parent, canvas, Screen.height).Width / parent.rect.width
        );
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
            "BPP.PreviewSwitches",
            parent,
            new(0, 0, parent.rect.width, 0)
        );
        _root = root.gameObject;
        _actions = root;
        var area = TouchScreen.Child(
            "Content",
            root,
            new(chrome.Padding, chrome.Gap, parent.rect.width - 2 * chrome.Padding, 0)
        );
        if (_skin == null || _skin.NeedsRefresh)
            _skin = new NativeSkin();
        var flow = new ChipFlow(area, platform.GameFont()!, chrome, 1, chrome.Target, _skin);
        if (upgrade)
            flow.Toggle(
                L.Resolve(UpgradeLabel),
                _host.UpgradeShown,
                _ => UiActions.Run("upgrade_preview", ToggleUpgrade)
            );
        if (_offersEnchantments)
            flow.Toggle(
                L.Resolve(EnchantmentsLabel),
                _expanded,
                _ => UiActions.Run("enchant_preview", ToggleEnchantments)
            );
        if (_expanded)
        {
            if (_effects is { Count: > 0 })
                flow.Definitions(_effects);
            else
                flow.Note(L.Resolve(EffectUnavailable), UiKit.MutedTextColor);
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
        if (_root != null)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
        }
        _root = null;
        _actions = null;
        _nativeScroll = null;
        _eventCamera = null;
    }

    private void Log(string action) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.preview.changed"),
            ("action", action),
            ("upgrade", _host.UpgradeShown),
            ("expanded", _expanded),
            ("effects", _effects?.Count ?? 0)
        );
}
