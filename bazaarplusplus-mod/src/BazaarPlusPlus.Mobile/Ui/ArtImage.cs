using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Ui;

// Wait for the load to finish before showing the text fallback so it cannot flash mid-load.
internal sealed class ArtImage(Image image, Action<bool>? fallback = null)
{
    private int _binding;

    public Image Image => image;

    public void Bind(Task<Sprite?>? sprite)
    {
        var binding = ++_binding;
        if (sprite == null || sprite.IsCompleted)
        {
            Show(sprite?.Status == TaskStatus.RanToCompletion ? sprite.Result : null, true);
            return;
        }
        Show(null, false);
        using var scope = MobilePlatform.Port.EnterMainThread();
        _ = Load(sprite, binding);
    }

    private async Task Load(Task<Sprite?> sprite, int binding)
    {
        Sprite? loaded = null;
        try
        {
            loaded = await sprite;
        }
        catch
        {
            // The loaders log their own failures; the row keeps its text.
        }
        if (image != null && binding == _binding)
            Show(loaded, true);
    }

    private void Show(Sprite? sprite, bool settled)
    {
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : Color.clear;
        fallback?.Invoke(settled && sprite == null);
    }
}
