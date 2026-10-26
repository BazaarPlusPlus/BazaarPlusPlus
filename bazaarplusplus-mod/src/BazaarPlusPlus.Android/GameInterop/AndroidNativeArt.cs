using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// Borrowed native sprites; never clones native controls or their callbacks. The sprites are
/// game assets that outlive any one view, so they are kept process-wide: a construction scans
/// for a pair only when it is missing or Unity has collected it. Main thread only.
/// </summary>
internal sealed class AndroidNativeArt
{
    public readonly record struct ButtonLayer(Sprite Sprite, Color Color, TouchRect Bounds);

    // Button with Selected and Frame with TagFrame come from one native object each, so each
    // pair is replaced together. Selected may legitimately be absent; Button and Frame decide.
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
            if (!Alive(_button))
            {
                scanned = true;
                ScanPopupButtons();
            }
            if (!Alive(_frame))
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

    public bool NeedsRefresh => !Alive(Button) || !Alive(Frame) || !Alive(TagFrame);

    /// <summary>
    /// The visible layers of the round <paramref name="gear"/> button except its icon, each
    /// placed as a fraction of the button's bounds; empty when they cannot be read.
    /// <paramref name="rect"/> is the gear's, and <paramref name="rootCanvas"/> its root canvas.
    /// </summary>
    public static IReadOnlyList<ButtonLayer> RoundLayersFor(
        UnityEngine.UI.Button gear,
        RectTransform rect,
        Canvas rootCanvas
    )
    {
        try
        {
            var native = gear.GetComponent<Il2Cpp.BazaarButtonController>();
            var bounds = AndroidNativeHud.ScreenBounds(rect, rootCanvas, 1);
            var layers = new List<ButtonLayer>();
            foreach (var image in gear.GetComponentsInChildren<Image>(true))
            {
                if (
                    !image.enabled
                    || !image.gameObject.activeInHierarchy
                    || image.sprite == null
                    || native?.ButtonIcon?.Pointer == image.Pointer
                )
                    continue;
                var area = AndroidNativeHud.ScreenBounds(image.rectTransform, rootCanvas, 1);
                layers.Add(
                    new(
                        image.sprite,
                        image.color,
                        new(
                            (area.Left - bounds.Left) / bounds.Width,
                            (area.Top - bounds.Top) / bounds.Height,
                            area.Width / bounds.Width,
                            area.Height / bounds.Height
                        )
                    )
                );
            }
            return layers;
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.round_failed"),
                exception
            );
            return [];
        }
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

    private static Sprite? Live(Sprite? sprite) => Alive(sprite) ? sprite : null;
}
