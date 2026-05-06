#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.ItemBoard;
using BazaarPlusPlus.Game.Settings;
using TheBazaar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class CardSetPreviewRuntime : MonoBehaviour
{
    private static readonly Vector2 DefaultOverlayAnchoredPosition = new(0f, -170f);
    private const float DefaultOverlayScale = 0.9f;
    private static readonly LocalizedTextSet SelectedSetLabel = new(
        "Selected Set",
        "当前卡组",
        "當前卡組",
        "當前卡組"
    );
    private static readonly LocalizedTextSet FinalBuildLabel = new(
        "Ten-Win Build",
        "十胜阵容",
        "十勝陣容",
        "十勝陣容"
    );
    private static readonly LocalizedTextSet NoMatchLabel = new(
        "No Match",
        "无匹配",
        "無匹配",
        "無匹配"
    );
    private static readonly LocalizedTextSet CandidateLabel = new(
        "Candidate",
        "候选",
        "候選",
        "候選"
    );
    private static readonly LocalizedTextSet NoCandidateWarningLabel = new(
        "No Match / No Candidate",
        "无匹配 / 无候选",
        "無匹配 / 無候選",
        "無匹配 / 無候選"
    );
    private static readonly LocalizedTextSet ModeEnabledLabel = new(
        "Selection Mode Active",
        "已进入选择模式",
        "已進入選擇模式",
        "已進入選擇模式"
    );
    private static readonly LocalizedTextSet DataFromLabel = new(
        "Data from",
        "数据来自",
        "資料來自",
        "資料來自"
    );

    private sealed class SelectedCardEntry
    {
        public string Key { get; set; } = string.Empty;

        public ItemBoardItemSpec Item { get; set; } = new ItemBoardItemSpec();
    }

    private readonly List<SelectedCardEntry> _selectedCards = new();
    private readonly ItemBoardService _itemBoard = new();
    private readonly CardSetBuildDataRepository _buildRepository = new();
    private readonly CardSetPreviewModeIndicator _modeIndicator = new();
    private CardSetPreviewSponsorSelection _currentSponsor = new();
    private bool _modeActive;
    private CardSetBuildRecommendationMode _displayMode =
        CardSetBuildRecommendationMode.SelectedSet;
    private int _recommendationIndex;

    public static CardSetPreviewRuntime? Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        HidePreview("awake");
    }

    private void OnDestroy()
    {
        _modeIndicator.Dispose();
        _itemBoard.Dispose();
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;

        if (
            keyboard?.capsLockKey.wasPressedThisFrame == true
            && keyboard.ctrlKey.isPressed == false
            && keyboard.altKey.isPressed == false
            && keyboard.shiftKey.isPressed == false
        )
        {
            ToggleMode();
        }

        if (!_modeActive)
            return;

        TryHandleDisplayModeSwitch(keyboard);
        TryHandleRecommendationNavigation(keyboard);
    }

    public bool TryHandleCardClick(CardController? controller, PointerEventData? eventData)
    {
        if (!_modeActive || controller?.CardData == null || eventData == null)
            return false;

        var card = controller.CardData;
        var button = eventData.button;
        if (
            button != PointerEventData.InputButton.Left
            && button != PointerEventData.InputButton.Right
        )
        {
            return true;
        }

        if (!TryBuildSelectedCardEntry(card, out var entry))
        {
            BppLog.Debug(
                "CardSetPreviewRuntime",
                $"Ignored non-renderable card modeActive={_modeActive} card={card.Template?.InternalName ?? "-"} templateId={card.TemplateId} type={card.Type}"
            );
            return true;
        }

        var changed =
            button == PointerEventData.InputButton.Left ? TryAdd(entry) : TryRemove(entry.Key);
        if (changed)
            StartRenderForSelection(controller, card);

        BppLog.Info(
            "CardSetPreviewRuntime",
            $"Handled click button={button} action={(button == PointerEventData.InputButton.Left ? "add" : "remove")} changed={changed} selected={_selectedCards.Count} card={card.Template?.InternalName ?? "-"} templateId={card.TemplateId}"
        );
        return true;
    }

    public bool ShouldSuppressNativeLockToggle(Card? card)
    {
        return _modeActive && card != null && card.Type == ECardType.Item;
    }

    private void ToggleMode()
    {
        _modeActive = !_modeActive;
        if (_modeActive)
        {
            _displayMode = CardSetBuildRecommendationMode.SelectedSet;
            _recommendationIndex = 0;
            ShowModeIndicator();
            BppLog.Info(
                "CardSetPreviewRuntime",
                "Selection mode enabled. Press A for Selected Set, D for Ten-Win Build, and W/S to browse matched builds."
            );
            return;
        }

        _selectedCards.Clear();
        _currentSponsor = new CardSetPreviewSponsorSelection();
        _recommendationIndex = 0;
        _modeIndicator.Hide();
        HidePreview("mode disabled");
        BppLog.Info("CardSetPreviewRuntime", "Selection mode disabled");
    }

    private bool TryHandleDisplayModeSwitch(Keyboard? keyboard)
    {
        if (
            !_modeActive
            || keyboard == null
            || keyboard.ctrlKey.isPressed
            || keyboard.altKey.isPressed
        )
        {
            return false;
        }

        var nextMode = CardSetPreviewHotkeys.ResolveDisplayMode(
            keyboard.aKey.wasPressedThisFrame,
            keyboard.dKey.wasPressedThisFrame
        );
        if (nextMode.HasValue)
            return TrySetDisplayMode(nextMode.Value);

        if (keyboard.tabKey.wasPressedThisFrame && keyboard.shiftKey.isPressed == false)
            return TrySetDisplayMode(CardSetBuildRecommendationModeFlow.GetNext(_displayMode));

        return false;
    }

    private bool TrySetDisplayMode(CardSetBuildRecommendationMode nextMode)
    {
        if (_displayMode == nextMode)
            return false;

        _displayMode = nextMode;
        _recommendationIndex = 0;
        var hero = Data.Run?.Player?.Hero.ToString() ?? "-";

        _currentSponsor = CardSetPreviewSponsorCatalog.PickDisplay();
        if (_selectedCards.Count > 0)
            _itemBoard.ShowTemplateSet(BuildCurrentRequest());
        ShowModeIndicator();

        BppLog.Info(
            "CardSetPreviewRuntime",
            $"Preview display source changed to {GetDisplayModeLabel(_displayMode)} hero={hero} selected={_selectedCards.Count}"
        );
        return true;
    }

    private bool TryHandleRecommendationNavigation(Keyboard? keyboard)
    {
        if (
            !_modeActive
            || keyboard == null
            || _displayMode == CardSetBuildRecommendationMode.SelectedSet
            || keyboard.ctrlKey.isPressed
            || keyboard.altKey.isPressed
        )
        {
            return false;
        }

        var delta = CardSetPreviewHotkeys.ResolveRecommendationDelta(
            keyboard.wKey.wasPressedThisFrame,
            keyboard.sKey.wasPressedThisFrame
        );

        if (delta == 0)
            return false;

        var recommendations = GetActiveRecommendations();
        if (recommendations.Count <= 1)
            return false;

        _recommendationIndex = WrapIndex(_recommendationIndex + delta, recommendations.Count);
        _currentSponsor = CardSetPreviewSponsorCatalog.PickDisplay();
        _itemBoard.ShowTemplateSet(BuildCurrentRequest());
        ShowModeIndicator();
        BppLog.Info(
            "CardSetPreviewRuntime",
            $"Recommendation index changed mode={GetDisplayModeLabel(_displayMode)} index={_recommendationIndex + 1}/{recommendations.Count}"
        );
        return true;
    }

    private void StartRenderForSelection(CardController controller, Card sourceCard)
    {
        if (_selectedCards.Count == 0)
        {
            _currentSponsor = new CardSetPreviewSponsorSelection();
            _recommendationIndex = 0;
            HidePreview("selection emptied");
            return;
        }

        _recommendationIndex = 0;
        _currentSponsor = CardSetPreviewSponsorCatalog.PickDisplay();
        ShowModeIndicator();
        if (_itemBoard.ShowTemplateSet(BuildCurrentRequest()))
            return;

        StartCoroutine(RenderWhenTooltipHostReady(controller, sourceCard));
    }

    private void HidePreview(string reason)
    {
        _itemBoard.Hide();
        BppLog.Info("CardSetPreviewRuntime", $"HidePreview reason={reason}");
    }

    private ItemBoardTemplateSetRequest BuildCurrentRequest()
    {
        var items = ResolvePreviewItems(
            out var modeLabel,
            out var hasRecommendation,
            out var resultIndex,
            out var resultCount,
            out var recommendationSource
        );
        return new ItemBoardTemplateSetRequest
        {
            Items = items,
            AnchoredPosition = DefaultOverlayAnchoredPosition,
            Scale = DefaultOverlayScale,
            SponsorText = BuildOverlayLabel(
                modeLabel,
                hasRecommendation,
                resultIndex,
                resultCount,
                recommendationSource
            ),
            SponsorName = _currentSponsor.Name,
            SponsorTier = _currentSponsor.Tier,
            CandidateIndex = resultIndex,
            CandidateCount = resultCount,
            IsAlertState =
                !hasRecommendation && _displayMode != CardSetBuildRecommendationMode.SelectedSet,
        };
    }

    private IReadOnlyList<ItemBoardItemSpec> ResolvePreviewItems(
        out string modeLabel,
        out bool hasRecommendation,
        out int resultIndex,
        out int resultCount,
        out string recommendationSource
    )
    {
        var selectedItems = _selectedCards.Select(card => card.Item.Clone()).ToList();
        modeLabel = GetDisplayModeLabel(_displayMode);
        hasRecommendation = false;
        resultIndex = 0;
        resultCount = 0;
        recommendationSource = string.Empty;
        if (_displayMode == CardSetBuildRecommendationMode.SelectedSet)
            return selectedItems;

        var recommendations = GetActiveRecommendations(selectedItems);
        if (recommendations.Count == 0)
            return selectedItems;

        hasRecommendation = true;
        _recommendationIndex = Mathf.Clamp(_recommendationIndex, 0, recommendations.Count - 1);
        var recommendation = recommendations[_recommendationIndex];
        modeLabel = recommendation.ModeLabel;
        resultIndex = recommendation.ResultIndex;
        resultCount = recommendation.ResultCount;
        recommendationSource = recommendation.Source;
        return recommendation.Items.Select(item => item.Clone()).ToList();
    }

    private IReadOnlyList<CardSetBuildRecommendation> GetActiveRecommendations(
        IReadOnlyList<ItemBoardItemSpec>? selectedItems = null
    )
    {
        if (_displayMode == CardSetBuildRecommendationMode.SelectedSet)
            return Array.Empty<CardSetBuildRecommendation>();

        selectedItems ??= _selectedCards.Select(card => card.Item.Clone()).ToList();
        var hero = Data.Run?.Player?.Hero.ToString();
        var selectedTemplateIds = selectedItems.Select(item => item.TemplateId).ToArray();
        return _buildRepository.FindFinalRecommendations(hero, selectedTemplateIds);
    }

    private string BuildOverlayLabel(
        string modeLabel,
        bool hasRecommendation,
        int resultIndex,
        int resultCount,
        string recommendationSource
    )
    {
        if (!hasRecommendation && _displayMode != CardSetBuildRecommendationMode.SelectedSet)
            return ResolveNoCandidateWarningLabel();

        var status = modeLabel;
        if (hasRecommendation && resultCount > 1)
            status = $"{status} | {ResolveCandidateLabel()} {resultIndex + 1}/{resultCount}";

        var label = string.IsNullOrWhiteSpace(_currentSponsor.Text)
            ? status
            : $"{status} | {_currentSponsor.Text}";
        if (hasRecommendation && !string.IsNullOrWhiteSpace(recommendationSource))
            label = $"{label} | {ResolveDataFromLabel()} {recommendationSource}";

        return label;
    }

    private void ShowModeIndicator()
    {
        if (!_modeActive)
        {
            _modeIndicator.Hide();
            return;
        }

        _modeIndicator.Show($"{ResolveModeEnabledLabel()} | {GetDisplayModeLabel(_displayMode)}");
    }

    private static int WrapIndex(int index, int count)
    {
        if (count <= 0)
            return 0;

        var wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private static string GetDisplayModeLabel(CardSetBuildRecommendationMode mode)
    {
        var languageCode = PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        return mode switch
        {
            CardSetBuildRecommendationMode.FinalBuild => FinalBuildLabel.Resolve(languageCode),
            _ => SelectedSetLabel.Resolve(languageCode),
        };
    }

    private static string ResolveNoMatchLabel()
    {
        return NoMatchLabel.Resolve(PlayerPreferences.Data?.LanguageCode ?? string.Empty);
    }

    private static string ResolveCandidateLabel()
    {
        return CandidateLabel.Resolve(PlayerPreferences.Data?.LanguageCode ?? string.Empty);
    }

    private static string ResolveNoCandidateWarningLabel()
    {
        return NoCandidateWarningLabel.Resolve(
            PlayerPreferences.Data?.LanguageCode ?? string.Empty
        );
    }

    private static string ResolveModeEnabledLabel()
    {
        return ModeEnabledLabel.Resolve(PlayerPreferences.Data?.LanguageCode ?? string.Empty);
    }

    private static string ResolveDataFromLabel()
    {
        return DataFromLabel.Resolve(PlayerPreferences.Data?.LanguageCode ?? string.Empty);
    }

    private bool TryAdd(SelectedCardEntry entry)
    {
        if (_selectedCards.Any(existing => existing.Key == entry.Key))
            return false;

        _selectedCards.Add(entry);
        return true;
    }

    private bool TryRemove(string key)
    {
        var index = _selectedCards.FindIndex(entry => entry.Key == key);
        if (index < 0)
            return false;

        _selectedCards.RemoveAt(index);
        return true;
    }

    private static bool TryBuildSelectedCardEntry(Card card, out SelectedCardEntry entry)
    {
        entry = null!;
        if (card == null)
            return false;

        if (card.Type != ECardType.Item)
            return false;

        entry = new SelectedCardEntry
        {
            Key = card.GetInstanceId().ToString(),
            Item = new ItemBoardItemSpec
            {
                TemplateId = card.TemplateId,
                Tier = card.Tier,
                EnchantmentType = card.GetEnchantment(),
                SocketId = card.LeftSocketId,
                Attributes =
                    card.Attributes != null
                        ? new Dictionary<ECardAttributeType, int>(card.Attributes)
                        : new Dictionary<ECardAttributeType, int>(),
            },
        };
        return true;
    }

    private System.Collections.IEnumerator RenderWhenTooltipHostReady(
        CardController controller,
        Card sourceCard
    )
    {
        const int maxFramesToWait = 10;
        var tooltipParent = Data.TooltipParentComponent;
        if (tooltipParent == null)
            yield break;

        var tooltipData = controller.GetTooltipData();
        tooltipParent.HideCardTooltipController();
        if (tooltipData != null)
            tooltipParent.ShowCardTooltipController(
                controller.transform,
                controller.TooltipOffset,
                tooltipData
            );

        for (var i = 0; i < maxFramesToWait; i++)
        {
            if (controller == null || controller.CardData != sourceCard)
                yield break;

            var tooltipController = tooltipParent.GetCardTooltipController(sourceCard);
            if (tooltipController == null)
            {
                yield return null;
                continue;
            }

            if (!_itemBoard.ShowTemplateSet(tooltipController, BuildCurrentRequest()))
            {
                yield break;
            }
            tooltipParent.HideCardTooltipController();
            yield break;
        }
    }
}
