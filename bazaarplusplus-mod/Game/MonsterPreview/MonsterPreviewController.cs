#pragma warning disable CS0436
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewController : MonoBehaviour
{
    private readonly List<PreviewCardSpec> _cards = new List<PreviewCardSpec>();
    private readonly List<PreviewCardSpec> _skillCards = new List<PreviewCardSpec>();

    private MonsterPreviewOverlayCoordinator _coordinator;
    private MonsterPreviewBoardRenderTarget _renderTarget;
    private IBoardAnchorStrategy _anchorStrategy;
    private PreviewBoardPresentation _presentation;
    private bool _visible;

    public bool Visible => _visible;

    private void Awake()
    {
        _presentation = new PreviewBoardPresentation();
        _renderTarget = new MonsterPreviewBoardRenderTarget();
        _coordinator = new MonsterPreviewOverlayCoordinator(_renderTarget);
        _coordinator.SetPresentation(_presentation);
        BppLog.Info(
            "MonsterPreviewController",
            "Awake completed; render target and coordinator created"
        );
    }

    private void LateUpdate()
    {
        if (!_visible)
        {
            _coordinator?.SetVisible(false);
            return;
        }

        _coordinator?.Tick();
    }

    public void SetAnchorStrategy(IBoardAnchorStrategy anchorStrategy)
    {
        _anchorStrategy = anchorStrategy;
        _coordinator?.SetAnchorStrategy(anchorStrategy);
        BppLog.Debug(
            "MonsterPreviewController",
            $"Anchor strategy set: {anchorStrategy?.GetType().Name ?? "null"}"
        );
    }

    public void SetPresentation(PreviewBoardPresentation presentation)
    {
        _presentation = presentation ?? new PreviewBoardPresentation();
        _coordinator?.SetPresentation(_presentation);
        BppLog.Debug(
            "MonsterPreviewController",
            $"Presentation updated: size={_presentation.BoardSize}, offset={_presentation.LocalOffset}, spacing={_presentation.CardSpacing}, scale={_presentation.CardScale}"
        );
    }

    public void SetCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _cards.Clear();
        if (cards != null)
            _cards.AddRange(CloneCards(cards));

        BppLog.Debug(
            "MonsterPreviewController",
            $"SetCards count={_cards.Count}, visible={_visible}"
        );
        _coordinator?.SetCards(_cards);
    }

    public void SetSkillCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        _skillCards.Clear();
        if (cards != null)
            _skillCards.AddRange(CloneCards(cards));

        BppLog.Debug(
            "MonsterPreviewController",
            $"SetSkillCards count={_skillCards.Count}, visible={_visible}"
        );
        _coordinator?.SetSkillCards(_skillCards);
    }

    public void SetDebugOptions(PreviewBoardDebugOptions debugOptions)
    {
        _coordinator?.SetDebugOptions(debugOptions);
    }

    public void ShowRequest(PreviewBoardRequest request)
    {
        _visible = request?.Presentation?.Visible ?? false;
        BppLog.Info(
            "MonsterPreviewController",
            $"ShowRequest visible={_visible} items={request?.InitialModel?.ItemCards?.Count ?? -1} skills={request?.InitialModel?.SkillCards?.Count ?? -1} hasDataSource={request?.DataSource != null} hasAnchor={request?.AnchorStrategy != null}"
        );
        _coordinator?.ShowRequest(request);
    }

    public void HidePreview()
    {
        BppLog.Info("MonsterPreviewController", "HidePreview called");
        SetVisible(false);
    }

    public void ClearCards()
    {
        _cards.Clear();
        _skillCards.Clear();
        BppLog.Debug("MonsterPreviewController", "ClearCards");
        _coordinator?.ClearCards();
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            BppLog.Debug("MonsterPreviewController", $"SetVisible ignored: already {visible}");
            return;
        }

        _visible = visible;
        BppLog.Info("MonsterPreviewController", $"SetVisible visible={_visible}");

        if (!_visible)
        {
            _coordinator?.SetVisible(false);
            return;
        }

        _coordinator?.SetVisible(true);
        if (_anchorStrategy != null)
            _coordinator?.SetAnchorStrategy(_anchorStrategy);
        _coordinator?.SetPresentation(_presentation);
        _coordinator?.SetCards(_cards);
        _coordinator?.SetSkillCards(_skillCards);
    }

    public void Refresh()
    {
        _coordinator?.Refresh();
    }

    private void OnDestroy()
    {
        _renderTarget?.Dispose();
        _renderTarget = null;
        _coordinator = null;
    }

    private static List<PreviewCardSpec> CloneCards(IReadOnlyList<PreviewCardSpec> cards)
    {
        return cards
            .Select(card => new PreviewCardSpec
            {
                TemplateId = card.TemplateId,
                Tier = card.Tier,
                SourceName = card.SourceName,
                Enchant = card.Enchant,
                Size = card.Size,
                Attributes =
                    card.Attributes != null
                        ? new Dictionary<int, int>(card.Attributes)
                        : new Dictionary<int, int>(),
            })
            .ToList();
    }
}
