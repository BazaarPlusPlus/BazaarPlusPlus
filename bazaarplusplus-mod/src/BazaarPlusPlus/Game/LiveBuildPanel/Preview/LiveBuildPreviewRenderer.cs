#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Preview;

internal sealed class LiveBuildPreviewRenderer : IDisposable
{
    private readonly Transform _parent;
    private readonly LiveBuildPanelView _view;
    private readonly Action<Guid> _toggle;
    private readonly Dictionary<BppItemBoardId, OwnedMonsterBoardPreview> _rows = new();
    private readonly Dictionary<BppItemBoardId, Rect> _bounds = new();
    private LiveBuildPanelSnapshot? _snapshot;
    private LiveItemBoardRowVm[] _rowModels = Array.Empty<LiveItemBoardRowVm>();
    private HashSet<Guid> _candidates = new();
    private readonly Dictionary<BppItemBoardId, int> _markerVersions = new();

    internal LiveBuildPreviewRenderer(
        Transform parent,
        LiveBuildPanelView view,
        Action<Guid> toggle
    )
    {
        _parent = parent;
        _view = view;
        _toggle = toggle;
    }

    internal void SetBounds(BppItemBoardId id, Rect bounds)
    {
        _bounds[id] = bounds;
        if (_rows.TryGetValue(id, out var row))
            row.SetBounds(bounds, _view.BoardFooterPixels, _view.BoardTopPixels);
    }

    internal void Render(LiveBuildPanelSnapshot snapshot)
    {
        _markerVersions.Clear();
        _snapshot = snapshot;
        _rowModels = snapshot.Rows;
        _candidates = new HashSet<Guid>(snapshot.CandidateTemplateIds);
        if (!_view.IsCreated)
            return;
        foreach (var row in _rowModels)
        {
            var board = row.Board;
            if (!_rows.TryGetValue(board.Id, out var preview))
            {
                preview = new OwnedMonsterBoardPreview(
                    _parent,
                    BppOverlaySorting.NativeCardPreview,
                    false,
                    (status, exception) =>
                    {
                        _view.SetBoardStatus(
                            board.Id,
                            status,
                            (exception as NativeBoardPartialFailure)?.Count ?? 0
                        );
                        if (exception != null)
                            LiveBuildPreviewLogWriter.ReportCardPreview(
                                new NativeCardPreviewFailure(
                                    NativeCardPreviewOperation.SetUp,
                                    NativeCardPreviewFailureReason.SetUpException,
                                    null,
                                    exception
                                )
                            );
                    },
                    itemsOnly: true
                );
                _rows.Add(board.Id, preview);
            }
            if (_bounds.TryGetValue(board.Id, out var bounds))
                preview.SetBounds(bounds, _view.BoardFooterPixels, _view.BoardTopPixels);
            preview.Render(
                board.Signature,
                NativeMonsterBoardItemMapper.Map(board, $"live-build-{board.Id}"),
                new()
            );
        }
        UpdateMarkers();
    }

    internal void Tick(Vector2? pointer, bool clicked)
    {
        if (_snapshot == null)
            return;
        if (!_view.IsCreated)
            return;
        if (_rows.Count == 0)
            Render(_snapshot);
        // Finish iterating before toggling: the callback may replace the recommendation.
        Guid? selected = null;
        foreach (var row in _rowModels)
        {
            if (!_rows.TryGetValue(row.Board.Id, out var preview))
                continue;
            preview.Fit();
            var template = preview.TrackPointer(pointer ?? new Vector2(-1, -1));
            if (clicked && row.CanToggleCandidates && template.HasValue)
                selected = template;
        }
        if (selected.HasValue)
            _toggle(selected.Value);
        UpdateMarkers();
    }

    private void UpdateMarkers()
    {
        if (_snapshot == null)
            return;
        foreach (var row in _rowModels)
        {
            var version = _rows.TryGetValue(row.Board.Id, out var boardPreview)
                ? boardPreview.GeometryVersion
                : -1;
            if (_markerVersions.TryGetValue(row.Board.Id, out var previous) && previous == version)
                continue;
            _markerVersions[row.Board.Id] = version;
            _view.BeginMarkers(row.Board.Id);
            if (_rows.TryGetValue(row.Board.Id, out var preview))
            {
                if (preview.TryGetCarpetBounds(out var carpet))
                    _view.SetBoardRail(row.Board.Id, carpet);
                preview.VisitItems(item =>
                {
                    if (_candidates.Contains(item.TemplateId))
                        _view.AddMarker(row.Board.Id, item.Bounds, row.CanToggleCandidates);
                });
            }
            _view.EndMarkers(row.Board.Id);
        }
    }

    internal void Hide()
    {
        foreach (var preview in _rows.Values)
            preview.Dispose();
        _rows.Clear();
        _markerVersions.Clear();
        _snapshot = null;
        _rowModels = Array.Empty<LiveItemBoardRowVm>();
        _candidates.Clear();
    }

    public void Dispose() => Hide();
}
