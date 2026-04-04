#pragma warning disable CS0436
namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class PreviewBoardSession
{
    private readonly IBoardRenderTarget _renderTarget;

    private PreviewBoardRequest _request;
    private string _lastSignature = string.Empty;
    private string _lastPresentationSignature = string.Empty;
    private BoardPose _lastPose;

    public PreviewBoardSession(IBoardRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;
    }

    public void Show(PreviewBoardRequest request)
    {
        _request = request;
        BppLog.Debug(
            "PreviewBoardSession",
            $"Show request received hasDataSource={request?.DataSource != null} hasAnchor={request?.AnchorStrategy != null} visible={request?.Presentation?.Visible ?? false}"
        );
    }

    public void Hide()
    {
        _renderTarget.SetVisible(false);
        _request = null;
        _lastSignature = string.Empty;
        _lastPresentationSignature = string.Empty;
        _lastPose = null;
        BppLog.Debug("PreviewBoardSession", "Hide called; cleared cached signature and pose");
    }

    public void Tick()
    {
        if (_request == null)
            return;

        var visible = _request.Presentation?.Visible ?? false;
        _renderTarget.SetVisible(visible);
        if (!visible)
        {
            BppLog.Debug("PreviewBoardSession", "Tick skipped because request is hidden");
            return;
        }

        var model = ResolveModel(_request);
        var pose = ResolvePose(_request);
        if (model == null || pose == null)
        {
            BppLog.Debug(
                "PreviewBoardSession",
                $"Tick skipped modelNull={model == null} poseNull={pose == null}"
            );
            return;
        }

        var signature = string.IsNullOrWhiteSpace(model.Signature)
            ? PreviewBoardSignature.Build(model)
            : model.Signature;
        var presentationSignature = BuildPresentationSignature(_request.Presentation);
        if (!ShouldRender(signature, presentationSignature, pose))
        {
            BppLog.Debug(
                "PreviewBoardSession",
                "Tick skipped because signature and pose are unchanged"
            );
            return;
        }

        model.Signature = signature;
        var renderModel = new BoardRenderModel
        {
            Data = model,
            Debug = _request.Debug,
            Pose = pose,
            Presentation = _request.Presentation,
        };
        _renderTarget.Render(renderModel);
        _lastSignature = signature;
        _lastPresentationSignature = presentationSignature;
        _lastPose = ClonePose(pose);
        BppLog.Debug(
            "PreviewBoardSession",
            $"Rendered signature={signature} pose={pose.Position} items={model.ItemCards?.Count ?? 0} skills={model.SkillCards?.Count ?? 0}"
        );
    }

    private static PreviewBoardModel ResolveModel(PreviewBoardRequest request)
    {
        if (
            request.DataSource != null
            && request.DataSource.TryBuild(out var model)
            && model != null
        )
            return model;

        return request.InitialModel;
    }

    private static BoardPose ResolvePose(PreviewBoardRequest request)
    {
        if (
            request.AnchorStrategy != null
            && request.AnchorStrategy.TryResolve(out var pose)
            && pose != null
        )
            return pose;

        return request.Pose;
    }

    private bool ShouldRender(string signature, string presentationSignature, BoardPose pose)
    {
        return signature != _lastSignature
            || presentationSignature != _lastPresentationSignature
            || !SamePose(_lastPose, pose);
    }

    private static bool SamePose(BoardPose left, BoardPose right)
    {
        if (left == null)
            return false;

        return left.Position == right.Position && left.Rotation == right.Rotation;
    }

    private static BoardPose ClonePose(BoardPose pose)
    {
        return new BoardPose { Position = pose.Position, Rotation = pose.Rotation };
    }

    private static string BuildPresentationSignature(PreviewBoardPresentation presentation)
    {
        presentation ??= new PreviewBoardPresentation();
        return string.Join(
            "|",
            presentation.Visible,
            presentation.DebugEnabled,
            presentation.ShowSkillBoard,
            presentation.ShowBrandingBoard,
            presentation.ShowMonsterInfoBoard,
            presentation.LocalOffset.x,
            presentation.LocalOffset.y,
            presentation.LocalOffset.z,
            presentation.CardScale.x,
            presentation.CardScale.y,
            presentation.CardScale.z,
            presentation.CardSpacing.x,
            presentation.CardSpacing.y,
            presentation.CardSpacing.z,
            presentation.BoardSize.x,
            presentation.BoardSize.y,
            presentation.SkillBoardWidth,
            presentation.BoardThickness,
            presentation.BorderThickness,
            presentation.BorderHeight
        );
    }
}
