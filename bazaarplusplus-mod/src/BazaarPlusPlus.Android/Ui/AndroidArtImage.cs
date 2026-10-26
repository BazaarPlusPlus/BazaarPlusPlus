using BazaarPlusPlus.Android.Host;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// A row's image bound to a sprite that may still be loading. A reused row drops a load it no
/// longer shows; <c>fallback</c> hears whether its text stand-in should show, which it does only
/// once a load ends without a sprite, so a first load does not flash the text.
/// </summary>
internal sealed class AndroidArtImage(Image image, Action<bool>? fallback = null)
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
        using var scope = AndroidMainThread.Host.Enter();
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
