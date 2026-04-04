#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterLockShowcaseRuntime : MonoBehaviour
{
    private readonly MonsterLockShowcaseController _controller =
        new MonsterLockShowcaseController();
    private readonly FixedAnchorStrategy _anchorStrategy = new FixedAnchorStrategy(
        MonsterPreviewDefaults.DefaultAnchorPose
    );
    private readonly PreviewBoardPresentation _presentation =
        MonsterPreviewDefaults.CreateShowcasePresentation();
    private readonly MonsterPreviewDebugTuner _tuner;

    private MonsterPreviewController _overlayController;
    private Card _lockedCard;
    private bool _closeOnNextClickArmed;
    private int _closeOnNextClickArmedFrame = -1;
    public static MonsterLockShowcaseRuntime Instance { get; private set; }

    public bool IsPreviewActive => _lockedCard != null;

    public FixedAnchorStrategy AnchorStrategy => _anchorStrategy;

    public PreviewBoardPresentation Presentation => _presentation;

    public MonsterPreviewDebugTuner DebugTuner => _tuner;

    public MonsterLockShowcaseRuntime()
    {
        _tuner = new MonsterPreviewDebugTuner(_anchorStrategy, _presentation);
    }

    private void Awake()
    {
        Instance = this;
        _overlayController = GetComponent<MonsterPreviewController>();
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Awake overlayControllerFound={_overlayController != null}"
        );
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private void Update()
    {
        if (!IsPreviewActive || !_closeOnNextClickArmed)
            return;

        var mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            TryConsumeNextClickToClosePreview(
                isLeftClick: true,
                isRightClick: false,
                reason: "next global left click"
            );
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            TryConsumeNextClickToClosePreview(
                isLeftClick: false,
                isRightClick: true,
                reason: "next global right click"
            );
        }
    }

    public bool HandleLockToggle(Card card)
    {
        if (_overlayController == null)
            return false;

        if (
            TryConsumeNextClickToClosePreview(
                isLeftClick: false,
                isRightClick: true,
                reason: "next right click"
            )
        )
        {
            return true;
        }

        if (IsPreviewActive)
        {
            HideOverlay("right click toggle");
            return true;
        }

        var isShowcaseCard = card != null && IsShowcaseCard(card);
        var isMonsterCard = card != null && IsMonsterSourceCard(card);
        if (!_controller.ShouldShowForLock(card?.TemplateId, isShowcaseCard, isMonsterCard))
            return false;

        if (!TryBuildPreview(card, out var previewModel, out var source))
            return false;

        _lockedCard = card;
        _anchorStrategy.SetPose(MonsterPreviewDefaults.DefaultAnchorPose);
        CopyPresentation(MonsterPreviewDefaults.CreateShowcasePresentation(), _presentation);
        _overlayController.ShowRequest(
            CreateShowcaseRequest(previewModel, card?.Template?.InternalName ?? source, source)
        );
        _closeOnNextClickArmed = true;
        _closeOnNextClickArmedFrame = Time.frameCount;
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Activated BPP showcase mode source={source} card={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={previewModel?.ItemCards?.Count ?? 0} skills={previewModel?.SkillCards?.Count ?? 0} armedFrame={_closeOnNextClickArmedFrame}"
        );
        return true;
    }

    public bool TryConsumeNextClickToClosePreview(
        PointerEventData.InputButton? button,
        string reason
    )
    {
        return TryConsumeNextClickToClosePreview(
            isLeftClick: button == PointerEventData.InputButton.Left,
            isRightClick: button == PointerEventData.InputButton.Right,
            reason: reason
        );
    }

    public void HandlePreviewModeChanged(bool useNativePreview)
    {
        if (!useNativePreview || !IsPreviewActive)
            return;

        HideOverlay("switched to native monster preview");
    }

    private void HideOverlay(string reason)
    {
        _lockedCard = null;
        _closeOnNextClickArmed = false;
        _closeOnNextClickArmedFrame = -1;
        if (_overlayController == null)
            return;

        _overlayController.ClearCards();
        _overlayController.HidePreview();
        BppLog.Info("MonsterLockShowcaseRuntime", $"Hiding preview: {reason}");
    }

    private static bool IsShowcaseCard(Card card)
    {
        var controller = Data.CardAndSkillLookup?.GetCardController(card);
        return controller != null && controller.GetComponent<ShowcaseCardMarker>() != null;
    }

    public bool ShouldInterceptLockToggle(Card card)
    {
        if (_overlayController == null)
            return false;

        var isShowcaseCard = card != null && IsShowcaseCard(card);
        var isMonsterCard = card != null && IsMonsterSourceCard(card);
        return _controller.ShouldInterceptLockToggle(
            IsPreviewActive,
            card != null,
            isShowcaseCard,
            isMonsterCard
        );
    }

    private static bool IsMonsterSourceCard(Card card)
    {
        if (card == null || !BppRuntimeHost.RunContext.IsInGameRun)
            return false;

        return BppRuntimeHost.MonsterCatalog.TryGetByEncounterId(card.TemplateId.ToString(), out _);
    }

    private static bool TryBuildPreview(
        Card card,
        out PreviewBoardModel previewModel,
        out string source
    )
    {
        previewModel = null;
        source = string.Empty;

        if (card == null || !BppRuntimeHost.RunContext.IsInGameRun)
            return false;

        if (
            BppRuntimeHost.MonsterCatalog.TryGetByEncounterId(
                card.TemplateId.ToString(),
                out var monster
            )
        )
        {
            var sourceModel = MonsterPreviewProjector.BuildModel(monster, "monster_db");
            var cards = PreviewCardSpecFilter.FilterLocallyRenderable(sourceModel.ItemCards);
            var skillCards = PreviewCardSpecFilter.FilterLocallyRenderable(sourceModel.SkillCards);
            source = $"monster_db:{monster.EncounterShortId}";
            previewModel = CreateFilteredPreviewModel(sourceModel, cards, skillCards);
            LogFilteredPreviewCounts(
                card,
                source,
                sourceModel.ItemCards?.Count ?? 0,
                cards.Count,
                sourceModel.SkillCards?.Count ?? 0,
                skillCards.Count
            );
            return cards.Count > 0 || skillCards.Count > 0;
        }

        return false;
    }

    private static void LogFilteredPreviewCounts(
        Card card,
        string source,
        int originalItemCount,
        int filteredItemCount,
        int originalSkillCount,
        int filteredSkillCount
    )
    {
        BppLog.Info(
            "MonsterLockShowcaseRuntime",
            $"Preview filter source={source} encounter={card?.Template?.InternalName ?? "-"} templateId={card?.TemplateId} items={filteredItemCount}/{originalItemCount} skills={filteredSkillCount}/{originalSkillCount}"
        );
    }

    private PreviewBoardRequest CreateShowcaseRequest(
        PreviewBoardModel previewModel,
        string title,
        string source
    )
    {
        var dataSource = new InMemoryPreviewDataSource();
        previewModel ??= new PreviewBoardModel();
        dataSource.SetCards(previewModel.ItemCards, previewModel.SkillCards);
        var metadata = new Dictionary<string, string>(
            previewModel.Metadata ?? new Dictionary<string, string>()
        )
        {
            ["source"] = source,
        };
        dataSource.SetMetadata(
            string.IsNullOrWhiteSpace(previewModel.Title) ? title : previewModel.Title,
            metadata
        );
        var presentation = ClonePresentation(_presentation);
        if (!presentation.Visible)
        {
            BppLog.Warn(
                "MonsterLockShowcaseRuntime",
                $"Showcase presentation was hidden before request creation; forcing visible source={source} title={title}"
            );
            presentation.Visible = true;
        }

        return new PreviewBoardRequest
        {
            DataSource = dataSource,
            AnchorStrategy = _anchorStrategy,
            Presentation = presentation,
            Debug = new PreviewBoardDebugOptions(),
        };
    }

    private bool TryConsumeNextClickToClosePreview(
        bool isLeftClick,
        bool isRightClick,
        string reason
    )
    {
        if (
            !_controller.ShouldConsumeNextClickToClosePreview(
                IsPreviewActive,
                _closeOnNextClickArmed,
                isLeftClick,
                isRightClick
            )
        )
        {
            return false;
        }

        if (!NextClickCloseFrameGate.CanConsume(_closeOnNextClickArmedFrame, Time.frameCount))
        {
            BppLog.Debug(
                "MonsterLockShowcaseRuntime",
                $"Ignored close consume in armed frame reason={reason} armedFrame={_closeOnNextClickArmedFrame} currentFrame={Time.frameCount}"
            );
            return false;
        }

        HideOverlay(reason);
        return true;
    }

    private static PreviewBoardModel CreateFilteredPreviewModel(
        PreviewBoardModel sourceModel,
        IReadOnlyList<PreviewCardSpec> cards,
        IReadOnlyList<PreviewCardSpec> skillCards
    )
    {
        var model = new PreviewBoardModel
        {
            Title = sourceModel?.Title ?? string.Empty,
            ItemCards = cards ?? new List<PreviewCardSpec>(),
            SkillCards = skillCards ?? new List<PreviewCardSpec>(),
            Metadata = new Dictionary<string, string>(
                sourceModel?.Metadata ?? new Dictionary<string, string>()
            ),
        };
        model.Signature = PreviewBoardSignature.Build(model);
        return model;
    }

    private static IReadOnlyDictionary<string, string> BuildEncounterPreviewMetadata(
        RunInfo.MonsterPreview preview,
        string source
    )
    {
        return new Dictionary<string, string>
        {
            ["source"] = source ?? string.Empty,
            ["encounter"] = preview?.EncounterShortId ?? string.Empty,
            ["health"] = preview?.Health?.ToString() ?? string.Empty,
            ["reward_gold"] = preview?.RewardGold?.ToString() ?? string.Empty,
            ["reward_xp"] = preview?.RewardXp?.ToString() ?? string.Empty,
        };
    }

    private static void CopyPresentation(
        PreviewBoardPresentation source,
        PreviewBoardPresentation destination
    )
    {
        destination.Visible = source.Visible;
        destination.DebugEnabled = source.DebugEnabled;
        destination.LocalOffset = source.LocalOffset;
        destination.CardScale = source.CardScale;
        destination.CardSpacing = source.CardSpacing;
        destination.BoardSize = source.BoardSize;
        destination.SkillBoardWidth = source.SkillBoardWidth;
        destination.BoardThickness = source.BoardThickness;
        destination.BorderThickness = source.BorderThickness;
        destination.BorderHeight = source.BorderHeight;
    }

    private static PreviewBoardPresentation ClonePresentation(PreviewBoardPresentation presentation)
    {
        presentation ??= new PreviewBoardPresentation();
        return new PreviewBoardPresentation
        {
            Visible = presentation.Visible,
            DebugEnabled = presentation.DebugEnabled,
            LocalOffset = presentation.LocalOffset,
            CardScale = presentation.CardScale,
            CardSpacing = presentation.CardSpacing,
            BoardSize = presentation.BoardSize,
            SkillBoardWidth = presentation.SkillBoardWidth,
            BoardThickness = presentation.BoardThickness,
            BorderThickness = presentation.BorderThickness,
            BorderHeight = presentation.BorderHeight,
        };
    }
}
