#nullable enable
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.ItemBoard;
using BazaarPlusPlus.Game.Tooltips;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class MonsterPreviewItemBoardRuntime : MonoBehaviour
{
    private static readonly PropertyInfo? CurrentEncounterDataObjectProperty = AccessTools.Property(
        typeof(EncounterController),
        "CurrentEncounterDataObject"
    );

    private static readonly FieldInfo? EncounterCarpetField = AccessTools.Field(
        typeof(EncounterAssetDataSO),
        "encounterCarpet"
    );

    private readonly ItemBoardService _itemBoard = new ItemBoardService();
    private bool _reportedInvalidAnchoredPositionConfig;
    private Card? _lockedCard;
    private bool _closeOnNextClickArmed;
    private int _closeOnNextClickArmedFrame = -1;
    private IBppServices? _services;

    public static MonsterPreviewItemBoardRuntime? Instance { get; private set; }

    public bool IsPreviewActive => _lockedCard != null;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        _itemBoard.Dispose();
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new System.ArgumentNullException(nameof(services));
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
                PointerEventData.InputButton.Left,
                "next global left click"
            );
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
            TryConsumeNextClickToClosePreview(
                PointerEventData.InputButton.Right,
                "next global right click"
            );
    }

    public bool ShouldInterceptLockToggle(Card? card)
    {
        if (!MonsterPreviewFeature.UseBoardOnlyNativePreview || card == null)
            return false;

        var controller = Data.CardAndSkillLookup?.GetCardController(card);
        return controller != null
            && controller.GetComponent<ShowcaseCardMarker>() == null
            && card.Type == BazaarGameShared.Domain.Core.Types.ECardType.CombatEncounter;
    }

    public bool HandleLockToggle(CardTooltipController? tooltipController)
    {
        if (
            TryConsumeNextClickToClosePreview(
                PointerEventData.InputButton.Right,
                "next right click"
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

        if (
            tooltipController == null
            || !TryBuildPayload(
                tooltipController,
                out var currentCard,
                out var monster,
                out var carpet
            )
            || currentCard == null
            || monster == null
            || !_itemBoard.EnsureHost(tooltipController)
        )
        {
            return false;
        }

        _itemBoard.Render(
            new ItemBoardRenderInput
            {
                Monster = monster,
                Carpet = carpet,
                AnchoredPosition = ResolveConfiguredAnchoredPosition(),
            }
        );
        _lockedCard = currentCard;
        _closeOnNextClickArmed = true;
        _closeOnNextClickArmedFrame = Time.frameCount;
        Data.TooltipParentComponent?.HideCardTooltipController();
        BppLog.Info(
            "MonsterPreviewItemBoardRuntime",
            $"Activated card={currentCard.Template?.InternalName ?? "-"} templateId={currentCard.TemplateId} carpet={(carpet != null ? carpet.name : "null")}"
        );
        return true;
    }

    public bool TryConsumeNextClickToClosePreview(
        PointerEventData.InputButton button,
        string reason
    )
    {
        if (!IsPreviewActive || !_closeOnNextClickArmed)
            return false;

        if (
            button != PointerEventData.InputButton.Left
            && button != PointerEventData.InputButton.Right
        )
            return false;

        if (!NextClickCloseFrameGate.CanConsume(_closeOnNextClickArmedFrame, Time.frameCount))
            return false;

        HideOverlay(reason);
        return true;
    }

    public bool ShouldSuppressTooltip(Card? card)
    {
        return MonsterPreviewFeature.UseBoardOnlyNativePreview
            && IsPreviewActive
            && card != null
            && card == _lockedCard;
    }

    public void HandlePreviewModeChanged()
    {
        if (IsPreviewActive)
            HideOverlay("preview mode changed");
    }

    private void HideOverlay(string reason)
    {
        _lockedCard = null;
        _closeOnNextClickArmed = false;
        _closeOnNextClickArmedFrame = -1;
        _itemBoard.Hide();
        BppLog.Info("MonsterPreviewItemBoardRuntime", $"Hide reason={reason}");
    }

    private Vector2? ResolveConfiguredAnchoredPosition()
    {
        var rawValue = _services?.Config.ItemBoardAnchoredPositionConfig?.Value;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            _reportedInvalidAnchoredPositionConfig = false;
            _itemBoard.ClearAnchoredPositionOverride();
            return null;
        }

        if (ItemBoardAnchoredPositionParser.IsAuto(rawValue))
        {
            _reportedInvalidAnchoredPositionConfig = false;
            _itemBoard.ClearAnchoredPositionOverride();
            return null;
        }

        if (ItemBoardAnchoredPositionParser.TryParse(rawValue, out var anchoredPosition))
        {
            _reportedInvalidAnchoredPositionConfig = false;
            return anchoredPosition;
        }

        if (!_reportedInvalidAnchoredPositionConfig)
        {
            _reportedInvalidAnchoredPositionConfig = true;
            _itemBoard.ClearAnchoredPositionOverride();
            BppLog.Warn(
                "MonsterPreviewItemBoardRuntime",
                $"Ignored invalid ItemBoard.AnchoredPosition='{rawValue}'. Use 'auto' or 'x,y'."
            );
        }

        return null;
    }

    private static bool TryBuildPayload(
        CardTooltipController tooltipController,
        out Card? currentCard,
        out TMonster? monster,
        out CarpetAssetDataSO? carpet
    )
    {
        currentCard = tooltipController.CurrentCard;
        monster = null;
        carpet = null;
        if (currentCard == null)
            return false;

        var cardController = Data.CardAndSkillLookup?.GetCardController(currentCard);
        if (cardController != null)
            NativeMonsterPreviewShowTooltipsPatch.TryAugmentCachedTooltipData(
                cardController,
                "board_only_overlay"
            );

        var tooltipData =
            tooltipController.CurrentTooltipData as CardTooltipData
            ?? cardController?.GetTooltipData() as CardTooltipData;
        monster = CardTooltipDataFactory.GetMonster(tooltipData!);
        if (monster == null)
            return false;

        carpet = ResolveCarpet(cardController as EncounterController);
        return true;
    }

    private static CarpetAssetDataSO? ResolveCarpet(EncounterController? encounterController)
    {
        if (encounterController == null)
            return null;

        var encounterData = CurrentEncounterDataObjectProperty?.GetValue(encounterController);
        return encounterData != null
            ? EncounterCarpetField?.GetValue(encounterData) as CarpetAssetDataSO
            : null;
    }
}
