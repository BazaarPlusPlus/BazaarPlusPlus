#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewBoardRenderTarget : IBoardRenderTarget
{
    private MonsterPreviewBoard _board;
    private readonly PreviewRenderGenerationGate _renderGate = new PreviewRenderGenerationGate();

    public MonsterPreviewBoardRenderTarget()
        : this(CreateBoard()) { }

    internal MonsterPreviewBoardRenderTarget(MonsterPreviewBoard board)
    {
        _board = board;
        BppLog.Debug(
            "MonsterPreviewBoardRenderTarget",
            $"Constructed boardExists={_board != null} boardAlive={_board?.IsAlive ?? false}"
        );
    }

    public void Dispose()
    {
        _renderGate.MarkDisposed();
        _board?.Dispose();
        _board = null;
    }

    public void Render(BoardRenderModel renderModel)
    {
        if (!EnsureBoard())
        {
            BppLog.Debug(
                "MonsterPreviewBoardRenderTarget",
                "Render skipped because board could not be created"
            );
            return;
        }

        renderModel ??= new BoardRenderModel();
        var visible = renderModel.Presentation?.Visible ?? false;
        var generation = _renderGate.BeginRender(visible);
        BppLog.Debug(
            "MonsterPreviewBoardRenderTarget",
            $"Render visible={visible} generation={generation} items={renderModel.Data?.ItemCards?.Count ?? 0} skills={renderModel.Data?.SkillCards?.Count ?? 0} pose={renderModel.Pose?.Position}"
        );
        _board.SetMonsterInfo(renderModel.Data ?? new PreviewBoardModel());
        _board.SetPresentation(renderModel.Presentation ?? new PreviewBoardPresentation());
        _board.SetDebugOptions(renderModel.Debug ?? new PreviewBoardDebugOptions());
        _board.UpdateAnchor(renderModel.Pose.Position, renderModel.Pose.Rotation);
        _board.SetVisible(visible);
        _ = _board.RebuildAsync(
            renderModel.Data?.ItemCards ?? new List<PreviewCardSpec>(),
            renderModel.Data?.SkillCards ?? new List<PreviewCardSpec>(),
            () => _renderGate.ShouldCancel(generation)
        );
    }

    public void SetVisible(bool visible)
    {
        if (!EnsureBoard())
        {
            BppLog.Debug(
                "MonsterPreviewBoardRenderTarget",
                $"SetVisible({visible}) skipped because board could not be created"
            );
            return;
        }

        if (!visible)
        {
            _renderGate.InvalidateForHide();
            _board.Clear();
        }
        else
        {
            _renderGate.MarkVisible();
        }
        _board.SetVisible(visible);
        BppLog.Debug("MonsterPreviewBoardRenderTarget", $"SetVisible visible={visible}");
    }

    private bool EnsureBoard()
    {
        if (_board != null && _board.IsAlive)
            return true;

        BppLog.Warn(
            "MonsterPreviewBoardRenderTarget",
            $"Board missing or dead; recreating boardExists={_board != null} boardAlive={_board?.IsAlive ?? false}"
        );
        _board = CreateBoard();
        return _board != null && _board.IsAlive;
    }

    private static MonsterPreviewBoard CreateBoard()
    {
        var board = new MonsterPreviewBoard(
            "MonsterPreviewBoard",
            new MonsterPreviewItemCardFactory(),
            new MonsterPreviewSkillCardFactory()
        );
        BppLog.Debug(
            "MonsterPreviewBoardRenderTarget",
            $"CreateBoard created boardAlive={board?.IsAlive ?? false}"
        );
        return board;
    }
}
