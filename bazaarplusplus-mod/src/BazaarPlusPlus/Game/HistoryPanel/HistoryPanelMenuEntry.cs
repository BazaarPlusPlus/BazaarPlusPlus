#nullable enable
using TheBazaar.UI;
using TheBazaar.UI.Menu;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMenuEntry : IDisposable
{
    private Sprite? _icon;

    private sealed class EntryData : PoolableUIData
    {
        internal Sprite? Icon { get; init; }
    }

    internal void Append(List<PoolableUIData> entries)
    {
        if (entries.Any(entry => entry is EntryData))
            return;
        var merchandiseIndex = entries.FindIndex(entry =>
            entry is TraversableScenesSO.TraversableScene scene
            && Uri.TryCreate(scene.OptionalLink, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, "shop.playthebazaar.com", StringComparison.OrdinalIgnoreCase)
        );
        entries.Insert(
            merchandiseIndex >= 0 ? merchandiseIndex + 1 : Math.Max(0, entries.Count - 1),
            new EntryData { Icon = LoadIcon() }
        );
    }

    private Sprite? LoadIcon()
    {
        if (_icon != null)
            return _icon;
        using var stream = typeof(HistoryPanelMenuEntry).Assembly.GetManifestResourceStream(
            "BazaarPlusPlus.Resources.HistoryPanel.history-menu.png"
        );
        if (stream == null)
            return null;
        using var bytes = new System.IO.MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }
        _icon = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f)
        );
        return _icon;
    }

    public void Dispose()
    {
        if (_icon == null)
            return;
        UnityEngine.Object.Destroy(_icon.texture);
        UnityEngine.Object.Destroy(_icon);
        _icon = null;
    }

    internal void Refresh(SceneSelectOptionContainer row)
    {
        if (row.Data is not EntryData entry)
            return;
        row._text.text = HistoryPanelText.Title();
        row.UpdateForTextSize(row._text);
        row._icon.sprite = entry.Icon;
        row._isSelected = false;
        row.HandleSelectedVisualState();
        row.gameObject.name = "BppHistoryMenuEntry";
    }

    internal bool Select(SceneSelectOptionContainer row, bool selected)
    {
        if (row.Data is not EntryData)
            return true;
        if (selected && !TheBazaar.Data.IsInCombat)
        {
            row.OnSelected?.Invoke();
            row._toggle.SetIsOnWithoutNotify(false);
            HistoryPanel.OpenFromDockEntry();
        }
        return false;
    }
}
