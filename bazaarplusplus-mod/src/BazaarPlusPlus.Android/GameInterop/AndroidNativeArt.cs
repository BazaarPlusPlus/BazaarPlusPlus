using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>Borrowed native sprites; never clones native controls or their callbacks.</summary>
internal sealed class AndroidNativeArt
{
    public readonly record struct ButtonLayer(Sprite Sprite, Color Color, TouchRect Bounds);

    public IReadOnlyList<ButtonLayer> RoundLayers { get; private set; } = [];
    public Sprite? Button { get; private set; }
    public Sprite? Selected { get; private set; }
    public Sprite? Frame { get; private set; }
    public Sprite? TagFrame { get; private set; }

    public AndroidNativeArt()
    {
        try
        {
            var gear = AndroidNativeHud.FindSettingsButton();
            if (gear != null)
            {
                var native = gear.GetComponent<Il2Cpp.BazaarButtonController>();
                var bounds = AndroidNativeHud.ScreenBounds(gear.GetComponent<RectTransform>(), 1);
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
                    var area = AndroidNativeHud.ScreenBounds(image.rectTransform, 1);
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
                RoundLayers = layers;
            }
            foreach (var popup in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.GenericPopup>())
            {
                if (popup._buttons == null)
                    continue;
                foreach (var button in popup._buttons)
                {
                    if (button == null || button.DefaultImage == null)
                        continue;
                    Button = button.DefaultImage;
                    Selected = button.ClickedImage;
                    break;
                }
                if (Button != null)
                    break;
            }
            foreach (
                var tooltip in Resources.FindObjectsOfTypeAll<Il2CppTheBazaar.UI.Tooltips.BaseTooltipController>()
            )
            {
                var frame = tooltip.bronzeFrameSettings?.BackgroundFrame;
                if (frame == null)
                    continue;
                Frame = frame;
                TagFrame = tooltip.bronzeFrameSettings?.TagFrame;
                break;
            }
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.skin_failed"),
                exception
            );
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.native_art.skin_loaded"),
            ("button", Button != null),
            ("round_layers", RoundLayers.Count),
            ("frame", Frame != null),
            ("tag_frame", TagFrame != null)
        );
    }

    public bool NeedsRefresh => Button == null || Frame == null || TagFrame == null;
}
