using BazaarPlusPlus.Android.Collection;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>Touch actions for the currently displayed native tooltip; owns no rented cards.</summary>
internal sealed class AndroidGameplayPreviews(AndroidOverlayPanels panels) : IDisposable
{
    private CardTooltipController? _controller;
    private CardTooltipData? _tooltip;
    private IReadOnlyList<EEnchantmentType> _candidates = [];
    private IReadOnlyList<(string Label, string Text)>? _effects;
    private GameObject? _root;
    private RectTransform? _actions;
    private ScrollRect? _nativeScroll;
    private Camera? _eventCamera;
    private bool _expanded;
    private bool _dirty;
    private string? _screen;

    public bool ContainsPointer()
    {
        var pointer = UnityEngine.InputSystem.Pointer.current;
        if (
            _root == null
            || !_root.activeInHierarchy
            || pointer == null
            || !Application.isFocused
            || Data.IsInCombat
        )
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

    public void Observe(CardTooltipController controller, CardTooltipData tooltip)
    {
        if (_tooltip?.CardInstance.Pointer != tooltip.CardInstance.Pointer)
        {
            _expanded = false;
            _effects = null;
        }
        _controller = controller;
        _tooltip = tooltip;
        _candidates = AndroidEnchantPreview.Candidates(tooltip);
        _dirty = true;
    }

    public void Tick()
    {
        if (_tooltip == null)
            return;
        var shown = Data.TooltipParentComponent?.CardTooltipController;
        if (
            Data.IsInCombat
            || panels.OpenPanelId != null
            || Settings.AndroidNativeSettingsSection.AnyVisible
            || shown == null
            || shown.Pointer != _controller?.Pointer
            || shown.CurrentCard?.Pointer != _tooltip.CardInstance.Pointer
        )
        {
            Clear("tooltip_closed");
            return;
        }
        var screen = AndroidTouchScreen.Key();
        if (screen != _screen)
        {
            _screen = screen;
            _dirty = true;
        }
        if (_dirty && AndroidGameFont.Font != null)
            Render();
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
        DestroyView();
        _controller = null;
        _tooltip = null;
        _effects = null;
        _expanded = false;
        _candidates = [];
        AndroidUpgradePreview.Release(reason);
    }

    public void Dispose() => Clear("stopped");

    private void ToggleUpgrade()
    {
        if (_tooltip == null || _controller == null)
            return;
        AndroidUpgradePreview.Toggle();
        var card = Data.CardAndSkillLookup?.GetCardController(_tooltip.CardInstance);
        if (card != null)
            _tooltip.CanFuse = card.CanFuse();
        _controller.ApplyPreviewTooltip(_tooltip);
        // ApplyPreviewTooltip restores legends only for locked previews. A touch tooltip
        // also needs its normal tray restored after RenderLegend hides it during refresh.
        if (!_controller.isLocked)
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
            _effects ??= AndroidEnchantPreview.Build(_tooltip, _candidates);
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
        var upgrade = AndroidUpgradePreview.CanToggle;
        if (!upgrade && _candidates.Count == 0)
            return;

        // Join the native description column so the switches and effects move and scroll
        // with the card, including the game's transition between inspected cards.
        _nativeScroll = scroll;
        var canvas = parent.GetComponentInParent<Canvas>(true).rootCanvas;
        _eventCamera =
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4L);
        parent.GetWorldCorners(corners);
        var left = RectTransformUtility.WorldToScreenPoint(_eventCamera, corners[0]);
        var right = RectTransformUtility.WorldToScreenPoint(_eventCamera, corners[3]);
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
        var root = AndroidTouchScreen.Child(
            "BPP.GameplayPreviews",
            parent,
            new(0, 0, parent.rect.width, 0)
        );
        _root = root.gameObject;
        _actions = root;
        var area = AndroidTouchScreen.Child(
            "Content",
            root,
            new(chrome.Padding, chrome.Gap, parent.rect.width - 2 * chrome.Padding, 0)
        );
        var skin = new AndroidNativeSkin();
        var flow = new AndroidChipFlow(area, AndroidGameFont.Font!, chrome, 1, chrome.Target, skin);
        if (upgrade)
            flow.Toggle(
                L.Resolve(new LocalizedTextSet("Upgrade preview", "升级预览", "升級預覽")),
                AndroidUpgradePreview.Selected,
                _ => AndroidUiActions.Run("upgrade_preview", ToggleUpgrade)
            );
        if (_candidates.Count > 0)
            flow.Toggle(
                L.Resolve(new LocalizedTextSet("Enchantments", "附魔效果", "附魔效果")),
                _expanded,
                _ => AndroidUiActions.Run("enchant_preview", ToggleEnchantments)
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
                    AndroidUiKit.MutedTextColor
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
            new BppLogEvent(BppLogFeatureScope.Android, "android.gameplay_preview.changed"),
            ("action", action),
            ("upgrade", AndroidUpgradePreview.Selected),
            ("expanded", _expanded),
            ("effects", _effects?.Count ?? 0)
        );
}
