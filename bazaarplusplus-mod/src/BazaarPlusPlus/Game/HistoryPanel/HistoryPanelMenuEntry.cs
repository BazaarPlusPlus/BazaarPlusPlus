#nullable enable
using System.Runtime.CompilerServices;
using TheBazaar.UI;
using TheBazaar.UI.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMenuEntry : IDisposable
{
    private readonly ConditionalWeakTable<PooledScrollRectBase, RowPitch> _pitches = new();
    private Sprite? _icon;

    // The pooler's measured spacing and row pitch, before the History row changed them.
    private sealed class RowPitch
    {
        internal float Spacing { get; init; }
        internal float ElementSize { get; init; }
    }

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

    // The History row makes the menu one row longer, which puts its last row (Exit Game) over the
    // MENU button that opens and closes it. This tightens the row pitch so the menu keeps the
    // height it has without the History row. The pooler measures its pitch only on its first
    // Initialize, so each pass starts again from the saved first measurement.
    internal void FitRows(PooledScrollRectBase pooler)
    {
        if (
            pooler.DataProvider is not NavigationMenuController menu
            || !pooler.ScrollRect.vertical
            || !pooler.ScrollRect.content.TryGetComponent<HorizontalOrVerticalLayoutGroup>(
                out var layout
            )
        )
            return;
        var original = _pitches.GetValue(
            pooler,
            _ => new RowPitch { Spacing = pooler._layoutSpacing, ElementSize = pooler.ElementSize }
        );
        var rows = menu._data.Count;
        var shrink =
            rows >= 2 && menu._data.Any(entry => entry is EntryData)
                ? original.ElementSize / (rows - 1)
                : 0f;
        layout.spacing = original.Spacing - shrink;
        pooler._layoutSpacing = original.Spacing - shrink;
        pooler.ElementSize = original.ElementSize - shrink;
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
