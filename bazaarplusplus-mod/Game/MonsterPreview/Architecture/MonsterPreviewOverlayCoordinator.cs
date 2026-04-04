#pragma warning disable CS0436
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewOverlayCoordinator
{
    private readonly IBoardRenderTarget _renderTarget;
    private readonly PreviewBoardSession _session;
    private readonly InMemoryPreviewDataSource _dataSource = new InMemoryPreviewDataSource();

    private IBoardAnchorStrategy _anchorStrategy;
    private PreviewBoardRequest _externalRequest;
    private PreviewBoardPresentation _presentation = new PreviewBoardPresentation();
    private PreviewBoardDebugOptions _debugOptions = new PreviewBoardDebugOptions();
    private bool _visible;

    public MonsterPreviewOverlayCoordinator(IBoardRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;
        _session = new PreviewBoardSession(renderTarget);
    }

    public bool Visible => _visible;

    public void SetAnchorStrategy(IBoardAnchorStrategy anchorStrategy)
    {
        _externalRequest = null;
        _anchorStrategy = anchorStrategy;
    }

    public void SetPresentation(PreviewBoardPresentation presentation)
    {
        _externalRequest = null;
        _presentation = presentation ?? new PreviewBoardPresentation();
        _presentation.Visible = _visible;
    }

    public void SetCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _externalRequest = null;
        _dataSource.SetCards(cards ?? new List<PreviewCardSpec>(), GetSkillCardsSnapshot());
    }

    public void SetDebugOptions(PreviewBoardDebugOptions debugOptions)
    {
        _externalRequest = null;
        _debugOptions = debugOptions ?? new PreviewBoardDebugOptions();
        _presentation.DebugEnabled = _debugOptions.Enabled;
    }

    public void SetSkillCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _externalRequest = null;
        _dataSource.SetCards(GetItemCardsSnapshot(), cards ?? new List<PreviewCardSpec>());
    }

    public void ClearCards()
    {
        _externalRequest = null;
        _dataSource.SetCards(new List<PreviewCardSpec>(), new List<PreviewCardSpec>());
    }

    public void ShowRequest(PreviewBoardRequest request)
    {
        _externalRequest = request;
        _visible = request?.Presentation?.Visible ?? false;
        if (_visible)
            _session.Show(request);
        else
            _session.Hide();

        _renderTarget.SetVisible(_visible);
        BppLog.Info(
            "MonsterPreviewOverlayCoordinator",
            $"ShowRequest visible={_visible} hasExternalRequest={_externalRequest != null}"
        );
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        _presentation.Visible = visible;
        if (!visible)
            _session.Hide();
    }

    public void Refresh()
    {
        if (!_visible)
            return;

        _session.Show(BuildRequestForCurrentMode());
    }

    public void Tick()
    {
        if (!_visible)
            return;

        _session.Show(BuildRequestForCurrentMode());
        _session.Tick();
    }

    private PreviewBoardRequest BuildRequestForCurrentMode()
    {
        if (_externalRequest != null)
            return _externalRequest;

        return BuildRequest();
    }

    private PreviewBoardRequest BuildRequest()
    {
        return new PreviewBoardRequest
        {
            DataSource = _dataSource,
            AnchorStrategy = _anchorStrategy,
            Presentation = _presentation,
            Debug = _debugOptions,
        };
    }

    private IReadOnlyList<PreviewCardSpec> GetItemCardsSnapshot()
    {
        _dataSource.TryBuild(out var model);
        return model?.ItemCards ?? new List<PreviewCardSpec>();
    }

    private IReadOnlyList<PreviewCardSpec> GetSkillCardsSnapshot()
    {
        _dataSource.TryBuild(out var model);
        return model?.SkillCards ?? new List<PreviewCardSpec>();
    }
}
