#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Preview;

internal sealed class LiveBuildPreviewRenderer : IDisposable
{
    private const int PreviewLayer = 30;
    private const int PreviewSortingOrder = BppOverlaySorting.NativeCardPreview;
    private readonly Dictionary<BppItemBoardId, LiveItemBoardRowPreview> _rows = new();

    public bool SetBounds(BppItemBoardId id, Rect bounds) => Row(id).SetBounds(bounds);

    public IEnumerator Render(LiveBuildPanelSnapshot snapshot)
    {
        yield return Row(BppItemBoardId.FinalBuild).Render(snapshot.FinalBuild);
        yield return Row(BppItemBoardId.LiveShop).Render(snapshot.Shop);
        yield return Row(BppItemBoardId.LiveBoard).Render(snapshot.Board);
        yield return Row(BppItemBoardId.LiveStash).Render(snapshot.Stash);
    }

    public void PollHover(Vector2 mousePixels)
    {
        foreach (var row in _rows.Values)
            row.PollHover(mousePixels);
    }

    public void Hide()
    {
        foreach (var row in _rows.Values)
            row.Hide();
    }

    public void Dispose()
    {
        foreach (var row in _rows.Values)
            row.Dispose();
        _rows.Clear();
    }

    private LiveItemBoardRowPreview Row(BppItemBoardId id)
    {
        if (!_rows.TryGetValue(id, out var row))
        {
            row = new LiveItemBoardRowPreview(id, PreviewLayer, PreviewSortingOrder);
            _rows[id] = row;
        }

        return row;
    }
}
