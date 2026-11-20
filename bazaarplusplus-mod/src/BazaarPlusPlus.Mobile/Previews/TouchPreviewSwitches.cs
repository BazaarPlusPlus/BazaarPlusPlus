using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Previews;

internal sealed class TouchPreviewSwitches : IDisposable
{
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

    private const int NoUpgradeToggle = -1;

    private readonly ITooltipPreviewHost _host;
    private object? _card;
    private object? _render;
    private bool _offersEnchantments;
    private IReadOnlyList<(string Label, string Text)>? _effects;
    private GameObject? _root;
    private RectTransform? _actions;
    private ScrollRect? _nativeScroll;
    private Camera? _eventCamera;

    private NativeSkin? _skin;
    private bool _expanded;
    private bool _dirty;
    private string? _screen;
    private int _upgradeToggleFrame = NoUpgradeToggle;

    public TouchPreviewSwitches(ITooltipPreviewHost host) =>
        _host = host ?? throw new ArgumentNullException(nameof(host));

    // The host uses this hit test to keep the native tooltip open after a switch tap.
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

    public void Clear(string reason)
    {
        var held = _card != null;
        DestroyView();
        _card = null;
        _render = null;
        _effects = null;
        _expanded = false;
        _offersEnchantments = false;
        _upgradeToggleFrame = NoUpgradeToggle;
        _host.Release(reason);
        if (held)
            Log(reason);
    }

    public void Dispose() => Clear("stopped");

    private void Follow()
    {
        var port = MobilePlatform.Port;
        if (
            port.OpenPanelId != null
            || !port.PlayState().ShowsPreviewSwitches
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
            _upgradeToggleFrame = NoUpgradeToggle;
        }
        if (_upgradeToggleFrame != NoUpgradeToggle && _upgradeToggleFrame != Time.frameCount)
            UiActions.Run("upgrade_preview", ApplyUpgradeToggle);
        if (!Equals(render, _render))
        {
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
        if (_dirty && port.GameFont() != null)
            Render();
    }

    // Defer resizing until the next frame so the game's late deselection check still
    // sees a same-frame press and release against the original tooltip bounds.
    private void ToggleUpgrade()
    {
        if (_card != null)
            _upgradeToggleFrame = Time.frameCount;
    }

    private void ApplyUpgradeToggle()
    {
        _upgradeToggleFrame = NoUpgradeToggle;
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
        var platform = MobilePlatform.Port;
        var upgrade = platform.PlayState().PreviewsUpgrade && _host.CanToggleUpgrade;
        if (!upgrade && !_offersEnchantments)
            return;

        // Join the native description column so the switches and effects move and scroll with
        // the card, including the game's transition between inspected cards.
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
                L.Resolve(TouchUpgradePreviewSetting.Label),
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
