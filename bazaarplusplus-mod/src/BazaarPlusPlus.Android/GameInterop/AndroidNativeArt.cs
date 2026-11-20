using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

internal sealed class AndroidNativeArt
{
    public readonly record struct ButtonLayer(Sprite Sprite, Color Color, TouchRect Bounds);

    public readonly record struct RoundArt(IReadOnlyList<ButtonLayer> Layers, TouchRect? Icon);

    // Keep both sprites from the same donor. An optional sprite that was never found does not
    // trigger a rescan.
    private static Sprite? _button;
    private static Sprite? _selected;
    private static Sprite? _frame;
    private static Sprite? _tagFrame;

    public Sprite? Button { get; }
    public Sprite? Selected { get; }
    public Sprite? Frame { get; }
    public Sprite? TagFrame { get; }

    public AndroidNativeArt()
    {
        var scanned = false;
        try
        {
            if (!Alive(_button) || Lost(_selected))
            {
                scanned = true;
                ScanPopupButtons();
            }
            if (!Alive(_frame) || Lost(_tagFrame))
            {
                scanned = true;
                ScanTooltipFrames();
            }
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.skin_failed"),
                exception
            );
        }
        Button = Live(_button);
        Selected = Live(_selected);
        Frame = Live(_frame);
        TagFrame = Live(_tagFrame);
        if (scanned)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.skin_loaded"),
                ("button", Button != null),
                ("frame", Frame != null),
                ("tag_frame", TagFrame != null)
            );
    }

    public static RoundArt RoundArtFor(
        UnityEngine.UI.Button gear,
        RectTransform rect,
        Canvas rootCanvas
    )
    {
        try
        {
            var native = gear.GetComponent<Il2Cpp.BazaarButtonController>();
            var resting = RestingLayers(gear, native, out var swapClones);
            var bounds = AndroidNativeHud.ScreenBounds(rect, rootCanvas, 1);
            TouchRect Relative(RectTransform part)
            {
                var area = AndroidNativeHud.ScreenBounds(part, rootCanvas, 1);
                return new(
                    (area.Left - bounds.Left) / bounds.Width,
                    (area.Top - bounds.Top) / bounds.Height,
                    area.Width / bounds.Width,
                    area.Height / bounds.Height
                );
            }
            var icon = native?.ButtonIcon;
            var layers = new List<ButtonLayer>();
            foreach (var image in gear.GetComponentsInChildren<Image>(true))
            {
                if (
                    !image.enabled
                    || !image.gameObject.activeInHierarchy
                    || icon?.Pointer == image.Pointer
                    || swapClones.Contains(image.gameObject.name)
                )
                    continue;
                var sprite = image.sprite;
                var color = image.color;
                if (resting.TryGetValue(image.Pointer, out var rest))
                {
                    if (!rest.Shown)
                        continue;
                    sprite = rest.Sprite ?? sprite;
                    if (rest.Opaque)
                        color.a = 1f;
                }
                if (sprite == null)
                    continue;
                layers.Add(new(sprite, color, Relative(image.rectTransform)));
            }
            return new(layers, icon != null ? Relative(icon.rectTransform) : null);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.round_failed"),
                exception
            );
            return new([], null);
        }
    }

    // Taps leave the touch gear Selected, and sprite swaps create transition clones. Read the
    // resting state instead.
    private static Dictionary<IntPtr, (bool Shown, Sprite? Sprite, bool Opaque)> RestingLayers(
        UnityEngine.UI.Button gear,
        Il2Cpp.BazaarButtonController? native,
        out HashSet<string> swapClones
    )
    {
        var resting = new Dictionary<IntPtr, (bool, Sprite?, bool)>();
        swapClones = new HashSet<string>(StringComparer.Ordinal);
        if (native?.image != null && native.DefaultImage != null)
            resting[native.image.Pointer] = (true, native.DefaultImage, false);
        var graphics =
            gear.GetComponent<Il2CppTheBazaar.UI.Components.BazaarSelectableGraphics>()?.Graphics;
        if (graphics == null)
            return resting;
        foreach (var graphic in graphics)
        {
            var target = graphic?._graphic;
            if (target == null)
                continue;
            switch (graphic!._transition)
            {
                case Il2CppTheBazaar
                    .UI
                    .Components
                    .BazaarSelectableGraphics
                    .BazaarTransition
                    .SpriteSwap:
                    resting[target.Pointer] = (true, graphic._originalSprite, true);
                    swapClones.Add(target.gameObject.name + "(Clone)");
                    break;
                case Il2CppTheBazaar
                    .UI
                    .Components
                    .BazaarSelectableGraphics
                    .BazaarTransition
                    .ActiveState:
                    resting[target.Pointer] = (false, null, false);
                    break;
            }
        }
        return resting;
    }

    private static void ScanPopupButtons()
    {
        _button = null;
        _selected = null;
        foreach (var popup in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.GenericPopup>())
        {
            if (popup._buttons == null)
                continue;
            foreach (var button in popup._buttons)
            {
                if (button == null || button.DefaultImage == null)
                    continue;
                _selected = button.ClickedImage;
                _button = button.DefaultImage;
                return;
            }
        }
    }

    private static void ScanTooltipFrames()
    {
        _frame = null;
        _tagFrame = null;
        foreach (
            var tooltip in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.UI.Tooltips.BaseTooltipController>()
        )
        {
            var frame = tooltip.bronzeFrameSettings?.BackgroundFrame;
            if (frame == null)
                continue;
            _tagFrame = tooltip.bronzeFrameSettings?.TagFrame;
            _frame = frame;
            return;
        }
    }

    private static bool Alive(Sprite? sprite) => sprite != null && !sprite.WasCollected;

    // Captured, then destroyed or collected; a reference check, so a never-found sprite is not lost.
    private static bool Lost(Sprite? sprite) => sprite is not null && !Alive(sprite);

    private static Sprite? Live(Sprite? sprite) => Alive(sprite) ? sprite : null;
}
