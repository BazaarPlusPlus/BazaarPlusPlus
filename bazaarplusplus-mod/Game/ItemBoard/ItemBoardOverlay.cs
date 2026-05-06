#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Players;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.UI.Tooltips;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.ItemBoard;

internal sealed class ItemBoardOverlay : IDisposable
{
    private sealed class OverlayRuntimeBehaviour : MonoBehaviour { }

    private const string SponsorPanelObjectName = "BppItemBoardSponsorPanel";
    private const string SponsorTextObjectName = "BppItemBoardSponsorText";
    private const float SponsorPanelFontSize = 17f;
    private static readonly Vector2 DefaultSponsorPanelSize = new(440f, 34f);
    private static readonly Vector2 DefaultSponsorPanelOffset = new(-325f, -315f);
    private const float DefaultSponsorPanelScale = 2f;

    private static readonly Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        "TheBazaar.UI.Tooltips.MonsterBoardTooltip"
    );

    private static readonly FieldInfo? MonsterBoardTooltipField = AccessTools.Field(
        typeof(CardTooltipController),
        "_monsterBoardTooltip"
    );

    private static readonly MethodInfo? HandlePoolingMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "HandlePooling")
            : null;

    private static readonly MethodInfo? AddCardMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "AddCard")
            : null;

    private static readonly MethodInfo? SetCarpetMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "SetCarpet")
            : null;

    private static readonly MethodInfo? ShowMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "Show")
            : null;

    private static readonly MethodInfo? HideMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "Hide")
            : null;

    private static readonly FieldInfo? SkillParentField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_skillParent")
            : null;

    private static readonly FieldInfo? SocketsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_sockets")
            : null;

    private static readonly FieldInfo? HealthTextField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_healthText")
            : null;

    private static readonly FieldInfo? CarpetImageField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_carpetImage")
            : null;

    private static readonly Type? CardPreviewBaseType = AccessTools.TypeByName(
        "TheBazaar.UI.CardPreviewBase"
    );

    private static readonly MethodInfo? CardPreviewShowMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Show") : null;
    private static readonly FieldInfo? SmallItemPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_smallItemPool")
            : null;
    private static readonly FieldInfo? MediumItemPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_mediumItemPool")
            : null;
    private static readonly FieldInfo? LargeItemPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_largeItemPool")
            : null;
    private static readonly FieldInfo? SkillPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_skillPool")
            : null;
    private static readonly FieldInfo? ActiveCardsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_activeCards")
            : null;
    private static readonly FieldInfo? ActiveSkillsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_activeSkills")
            : null;
    private static readonly object SponsorDebugSyncRoot = new();
    private static Vector2 _sponsorPanelDebugOffset = DefaultSponsorPanelOffset;
    private static float _sponsorPanelDebugScale = DefaultSponsorPanelScale;
    private static TMP_FontAsset? _resolvedSponsorFont;
    private static Material? _resolvedSponsorFontMaterial;
    private static bool _resolvedSponsorFontLogged;
    private static string? _resolvedSponsorFontSourcePath;
    private Image? _sponsorPanelBackground;
    private Outline? _sponsorPanelOutline;

    private GameObject? _overlayRoot;
    private RectTransform? _overlayRootRect;
    private OverlayRuntimeBehaviour? _runtimeBehaviour;
    private Component? _view;
    private RectTransform? _viewRect;
    private RectTransform? _sponsorPanelRect;
    private TextMeshProUGUI? _sponsorText;
    private Transform? _hostRoot;
    private bool _hasPinnedAnchoredPosition;
    private Vector2 _pinnedAnchoredPosition;
    private ItemBoardTemplateSetRequest? _currentRequest;
    private Coroutine? _revealCoroutine;
    private int _revealRevision;

    public bool IsAlive => _overlayRoot != null && _view != null;

    public bool Ensure(CardTooltipController controller)
    {
        if (controller == null)
            return false;

        var sourceView = MonsterBoardTooltipField?.GetValue(controller) as Component;
        if (sourceView == null)
        {
            BppLog.Warn(
                "ItemBoardOverlay",
                "Ensure failed because source MonsterBoardTooltip was null"
            );
            return false;
        }

        var rootCanvas = controller.RootCanvas;
        if (rootCanvas == null)
        {
            BppLog.Warn("ItemBoardOverlay", "Ensure failed because tooltip root canvas was null");
            return false;
        }

        if (!ReferenceEquals(_hostRoot, rootCanvas) || !IsAlive)
            CreateOverlay(sourceView, rootCanvas);

        UpdatePlacement(sourceView);
        return IsAlive;
    }

    public void Render(ItemBoardRenderInput input)
    {
        if (!IsAlive || _view == null || input?.Monster == null)
            return;

        var monster = input.Monster;
        ConfigureItemsOnlyVisuals();
        FlushRenderedCards();
        HandlePoolingMethod?.Invoke(_view, null);
        RenderItems(monster.Player.Hand.Items);
        if (input.Carpet != null)
            SetCarpetMethod?.Invoke(_view, new object[] { input.Carpet });

        if (input.AnchoredPosition.HasValue)
            SetAnchoredPosition(input.AnchoredPosition.Value);

        SetScale(input.Scale);
        UpdateSponsorVisual(
            _currentRequest?.SponsorText,
            _currentRequest?.SponsorName,
            _currentRequest?.SponsorTier ?? 0,
            _currentRequest?.CandidateIndex ?? 0,
            _currentRequest?.CandidateCount ?? 0,
            input.Scale
        );
        _overlayRoot!.SetActive(true);
        _overlayRoot.transform.SetAsLastSibling();
        _view.gameObject.SetActive(true);
        ShowMethod?.Invoke(_view, new object[] { input.ShowTime });
        ScheduleRevealPasses();
        BppLog.Info(
            "ItemBoardOverlay",
            $"Render monster={monster.InternalName ?? monster.Id.ToString()} carpet={(input.Carpet != null ? input.Carpet.name : "null")} items={monster.Player?.Hand?.Items?.Count ?? 0} anchored={_viewRect?.anchoredPosition}"
        );
    }

    public void RenderTemplateSet(ItemBoardTemplateSetRequest request)
    {
        if (!IsAlive || request == null)
            return;

        _currentRequest = request.Clone();
        var items =
            _currentRequest.Items?.Where(item => item?.TemplateId != Guid.Empty).ToList()
            ?? new List<ItemBoardItemSpec>();
        if (items.Count == 0)
        {
            Hide();
            return;
        }

        Render(
            new ItemBoardRenderInput
            {
                Monster = BuildSyntheticMonster(items),
                AnchoredPosition = _currentRequest.AnchoredPosition,
                Scale = _currentRequest.Scale,
                ShowTime = _currentRequest.ShowTime,
            }
        );
    }

    public void SetAnchoredPosition(Vector2 anchoredPosition)
    {
        _hasPinnedAnchoredPosition = true;
        _pinnedAnchoredPosition = anchoredPosition;
        if (_viewRect != null)
            _viewRect.anchoredPosition = anchoredPosition;

        UpdateSponsorPlacement(_viewRect?.localScale.x ?? 1f);
    }

    public void ClearAnchoredPositionOverride()
    {
        _hasPinnedAnchoredPosition = false;
    }

    public void SetScale(float scale)
    {
        if (_viewRect == null)
            return;

        var clamped = Mathf.Clamp(scale, 0.2f, 2f);
        _viewRect.localScale = Vector3.one * clamped;
        UpdateSponsorPlacement(clamped);
    }

    public void Hide(float hideTime = 0f)
    {
        if (!IsAlive || _view == null || _overlayRoot == null)
            return;

        StopRevealCoroutine();
        HideMethod?.Invoke(_view, new object[] { hideTime });
        _overlayRoot.SetActive(false);
        BppLog.Info("ItemBoardOverlay", "Hide");
    }

    public void ForceRevealCards()
    {
        if (_overlayRoot == null || CardPreviewBaseType == null || CardPreviewShowMethod == null)
            return;

        var previewCards = _overlayRoot.GetComponentsInChildren(CardPreviewBaseType, false);
        foreach (var previewCard in previewCards)
        {
            if (previewCard == null || !previewCard.gameObject.activeInHierarchy)
                continue;

            CardPreviewShowMethod.Invoke(previewCard, new object[] { true });
        }
    }

    private void ScheduleRevealPasses()
    {
        if (_runtimeBehaviour == null)
            return;

        StopRevealCoroutine();
        _revealRevision++;
        _revealCoroutine = _runtimeBehaviour.StartCoroutine(
            RevealCardsAfterFrames(_revealRevision)
        );
    }

    private void StopRevealCoroutine()
    {
        if (_runtimeBehaviour == null || _revealCoroutine == null)
            return;

        _runtimeBehaviour.StopCoroutine(_revealCoroutine);
        _revealCoroutine = null;
    }

    private System.Collections.IEnumerator RevealCardsAfterFrames(int revision)
    {
        yield return null;
        if (revision != _revealRevision)
            yield break;

        ForceRevealCards();
        yield return null;
        if (revision != _revealRevision)
            yield break;

        ForceRevealCards();
        yield return null;
        if (revision != _revealRevision)
            yield break;

        ForceRevealCards();
        _revealCoroutine = null;
    }

    public void Dispose()
    {
        StopRevealCoroutine();
        if (_overlayRoot != null)
            UnityEngine.Object.Destroy(_overlayRoot);

        _overlayRoot = null;
        _overlayRootRect = null;
        _runtimeBehaviour = null;
        _view = null;
        _viewRect = null;
        _sponsorPanelRect = null;
        _sponsorText = null;
        _sponsorPanelBackground = null;
        _sponsorPanelOutline = null;
        _hostRoot = null;
        _hasPinnedAnchoredPosition = false;
        _pinnedAnchoredPosition = default;
        _currentRequest = null;
    }

    private void CreateOverlay(Component sourceView, Transform hostRoot)
    {
        Dispose();

        _hostRoot = hostRoot;
        _overlayRoot = new GameObject("BppItemBoardOverlay", typeof(RectTransform));
        _overlayRootRect = _overlayRoot.GetComponent<RectTransform>();
        _runtimeBehaviour = _overlayRoot.AddComponent<OverlayRuntimeBehaviour>();
        _overlayRootRect.SetParent(hostRoot, false);
        _overlayRootRect.anchorMin = Vector2.zero;
        _overlayRootRect.anchorMax = Vector2.one;
        _overlayRootRect.offsetMin = Vector2.zero;
        _overlayRootRect.offsetMax = Vector2.zero;
        _overlayRootRect.pivot = new Vector2(0.5f, 0.5f);

        var clone = UnityEngine.Object.Instantiate(sourceView.gameObject, _overlayRootRect, false);
        clone.name = "BppItemBoardTooltip";
        _view = clone.GetComponent(
            MonsterBoardTooltipType?.FullName ?? "TheBazaar.UI.Tooltips.MonsterBoardTooltip"
        );
        _viewRect = clone.GetComponent<RectTransform>();
        if (_viewRect != null)
        {
            _viewRect.anchorMin = new Vector2(0.5f, 0.5f);
            _viewRect.anchorMax = new Vector2(0.5f, 0.5f);
            _viewRect.pivot = new Vector2(0.5f, 0.5f);
        }

        ConfigureItemsOnlyVisuals();
        CreateSponsorPanel();

        _overlayRoot.SetActive(false);
        BppLog.Info(
            "ItemBoardOverlay",
            $"Created overlay hostRoot={hostRoot.name} cloneAlive={_view != null}"
        );
    }

    private void UpdatePlacement(Component sourceView)
    {
        if (_overlayRootRect == null || _viewRect == null || _hostRoot == null)
            return;

        if (_hasPinnedAnchoredPosition)
        {
            _viewRect.anchoredPosition = _pinnedAnchoredPosition;
            UpdateSponsorPlacement(_viewRect.localScale.x);
            return;
        }

        var sourceRect = sourceView.GetComponent<RectTransform>();
        if (sourceRect == null)
        {
            _viewRect.anchoredPosition = new Vector2(260f, -20f);
            UpdateSponsorPlacement(_viewRect.localScale.x);
            return;
        }

        _viewRect.sizeDelta = sourceRect.rect.size;
        var canvas = _hostRoot.GetComponent<Canvas>();
        var camera =
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, sourceRect.position);
        if (
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayRootRect,
                screenPoint,
                camera,
                out var localPoint
            )
        )
        {
            _viewRect.anchoredPosition = localPoint;
            UpdateSponsorPlacement(_viewRect.localScale.x);
            return;
        }

        _viewRect.anchoredPosition = new Vector2(260f, -20f);
        UpdateSponsorPlacement(_viewRect.localScale.x);
    }

    private void ConfigureItemsOnlyVisuals()
    {
        if (_view == null)
            return;

        if (SkillParentField?.GetValue(_view) is RectTransform skillParent)
            skillParent.gameObject.SetActive(false);

        var carpetTransform = (CarpetImageField?.GetValue(_view) as Component)?.transform;
        if (HealthTextField?.GetValue(_view) is TMP_Text healthText)
            HideHealthVisuals(healthText.transform, carpetTransform);
    }

    private void FlushRenderedCards()
    {
        if (_view == null)
            return;

        FlushPool(SmallItemPoolField);
        FlushPool(MediumItemPoolField);
        FlushPool(LargeItemPoolField);
        FlushPool(SkillPoolField);
        ClearListField(ActiveCardsField);
        ClearListField(ActiveSkillsField);

        if (SocketsField?.GetValue(_view) is RectTransform[] sockets)
        {
            foreach (var socket in sockets)
                FlushChildren(socket);
        }

        if (SkillParentField?.GetValue(_view) is RectTransform skillParent)
            FlushChildren(skillParent);
    }

    private void FlushPool(FieldInfo? poolField)
    {
        if (poolField?.GetValue(_view) is not System.Collections.IEnumerable pool)
            return;

        foreach (var entry in pool)
        {
            if (entry is not Component component)
                continue;

            component.transform.localScale = Vector3.one;
            component.gameObject.SetActive(false);
        }
    }

    private void ClearListField(FieldInfo? listField)
    {
        if (listField?.GetValue(_view) is System.Collections.IList list)
            list.Clear();
    }

    private static void FlushChildren(Transform? parent)
    {
        if (parent == null)
            return;

        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (child == null)
                continue;

            child.localScale = Vector3.one;
            child.gameObject.SetActive(false);
        }
    }

    private void RenderItems(IEnumerable<TCardInstanceItem>? items)
    {
        if (_view == null || AddCardMethod == null || items == null)
            return;

        foreach (var item in items)
        {
            if (item != null)
                AddCardMethod.Invoke(_view, new object[] { item });
        }
    }

    private void CreateSponsorPanel()
    {
        if (_overlayRootRect == null || _sponsorPanelRect != null)
            return;

        var sponsorPanelObject = new GameObject(
            SponsorPanelObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        _sponsorPanelRect = sponsorPanelObject.GetComponent<RectTransform>();
        _sponsorPanelRect.SetParent(_overlayRootRect, false);
        _sponsorPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        _sponsorPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        _sponsorPanelRect.pivot = new Vector2(0f, 0.5f);
        _sponsorPanelRect.sizeDelta = DefaultSponsorPanelSize;

        _sponsorPanelBackground = sponsorPanelObject.GetComponent<Image>();
        _sponsorPanelBackground.color = new Color(0.09f, 0.09f, 0.12f, 0.82f);
        _sponsorPanelBackground.raycastTarget = false;

        _sponsorPanelOutline = sponsorPanelObject.GetComponent<Outline>();
        _sponsorPanelOutline.effectColor = new Color(0.98f, 0.79f, 0.42f, 0.66f);
        _sponsorPanelOutline.effectDistance = new Vector2(1.25f, -1.25f);
        _sponsorPanelOutline.useGraphicAlpha = true;

        var sponsorTextObject = new GameObject(
            SponsorTextObjectName,
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        var sponsorTextRect = sponsorTextObject.GetComponent<RectTransform>();
        sponsorTextRect.SetParent(_sponsorPanelRect, false);
        sponsorTextRect.anchorMin = Vector2.zero;
        sponsorTextRect.anchorMax = Vector2.one;
        sponsorTextRect.offsetMin = new Vector2(10f, 4f);
        sponsorTextRect.offsetMax = new Vector2(-10f, -4f);

        _sponsorText = sponsorTextObject.GetComponent<TextMeshProUGUI>();
        ApplySponsorTextStyle(_sponsorText);
        _sponsorText.fontSize = SponsorPanelFontSize;
        _sponsorText.enableAutoSizing = true;
        _sponsorText.fontSizeMin = 11f;
        _sponsorText.fontSizeMax = SponsorPanelFontSize;
        _sponsorText.alignment = TextAlignmentOptions.MidlineLeft;
        _sponsorText.color = new Color(0.98f, 0.93f, 0.84f, 1f);
        _sponsorText.textWrappingMode = TextWrappingModes.NoWrap;
        _sponsorText.overflowMode = TextOverflowModes.Ellipsis;
        _sponsorText.raycastTarget = false;

        ApplySponsorPanelMetrics();
        sponsorPanelObject.SetActive(false);
    }

    private void UpdateSponsorVisual(
        string? sponsorText,
        string? sponsorName,
        int sponsorTier,
        int candidateIndex,
        int candidateCount,
        float scale
    )
    {
        if (_sponsorPanelRect == null || _sponsorText == null)
            return;

        var hasSponsorText = !string.IsNullOrWhiteSpace(sponsorText);
        _sponsorPanelRect.gameObject.SetActive(hasSponsorText);
        if (!hasSponsorText)
            return;

        ApplySponsorTextStyle(_sponsorText);
        ApplySponsorTextColor(
            sponsorText ?? string.Empty,
            sponsorName,
            sponsorTier,
            candidateIndex,
            candidateCount,
            _currentRequest?.IsAlertState == true
        );
        UpdateSponsorPlacement(scale);
    }

    private void UpdateSponsorPlacement(float scale)
    {
        if (_sponsorPanelRect == null || _viewRect == null)
            return;

        var clamped = Mathf.Clamp(scale, 0.2f, 2f);
        _sponsorPanelRect.anchoredPosition =
            _viewRect.anchoredPosition + (_sponsorPanelDebugOffset * clamped);
        _sponsorPanelRect.localScale = Vector3.one * (clamped * _sponsorPanelDebugScale);
    }

    private void ApplySponsorTextStyle(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        ResolveSponsorTextStyle(text.text);
        text.font = _resolvedSponsorFont ?? TMP_Settings.defaultFontAsset;
        if (_resolvedSponsorFontMaterial != null)
            text.fontSharedMaterial = _resolvedSponsorFontMaterial;

        text.richText = false;
    }

    private void ApplySponsorPanelMetrics()
    {
        if (_sponsorPanelRect != null)
            _sponsorPanelRect.sizeDelta = DefaultSponsorPanelSize;

        if (_sponsorText != null)
        {
            _sponsorText.fontSize = SponsorPanelFontSize;
            _sponsorText.fontSizeMax = SponsorPanelFontSize;
        }
    }

    private void ApplySponsorTextColor(
        string sponsorText,
        string? sponsorName,
        int sponsorTier,
        int candidateIndex,
        int candidateCount,
        bool isAlertState
    )
    {
        if (_sponsorText == null)
            return;

        ApplySponsorPanelChrome(isAlertState);

        if (isAlertState)
        {
            _sponsorText.color = new Color(1f, 0.87f, 0.82f, 1f);
            _sponsorText.richText = false;
            _sponsorText.text = sponsorText;
            return;
        }

        var highlightColor = ResolveSponsorTextColor(sponsorTier, candidateIndex, candidateCount);
        var baseColor = new Color(0.98f, 0.93f, 0.84f, 1f);
        _sponsorText.color = baseColor;
        _sponsorText.richText = true;
        _sponsorText.text = BuildSponsorRichText(
            sponsorText,
            sponsorName,
            highlightColor,
            candidateIndex,
            candidateCount
        );
    }

    private void ApplySponsorPanelChrome(bool isAlertState)
    {
        if (_sponsorPanelBackground != null)
        {
            _sponsorPanelBackground.color = isAlertState
                ? new Color(0.28f, 0.08f, 0.08f, 0.92f)
                : new Color(0.09f, 0.09f, 0.12f, 0.82f);
        }

        if (_sponsorPanelOutline == null)
            return;

        _sponsorPanelOutline.effectColor = isAlertState
            ? new Color(1f, 0.36f, 0.30f, 0.86f)
            : new Color(0.98f, 0.79f, 0.42f, 0.66f);
        _sponsorPanelOutline.effectDistance = new Vector2(1.25f, -1.25f);
        _sponsorPanelOutline.useGraphicAlpha = true;
    }

    private static Color ResolveSponsorTextColor(
        int sponsorTier,
        int candidateIndex,
        int candidateCount
    )
    {
        return sponsorTier switch
        {
            4 => new Color(1f, 0.90f, 0.48f, 1f),
            3 => new Color(0.86f, 0.93f, 1f, 1f),
            2 => new Color(0.93f, 0.72f, 0.50f, 1f),
            _ => new Color(0.96f, 0.95f, 0.93f, 1f),
        };
    }

    private static string BuildSponsorRichText(
        string sponsorText,
        string? sponsorName,
        Color sponsorColor,
        int candidateIndex,
        int candidateCount
    )
    {
        if (string.IsNullOrWhiteSpace(sponsorText))
            return sponsorText;

        var result = sponsorText;
        if (!string.IsNullOrWhiteSpace(sponsorName))
            result = ReplaceLast(result, sponsorName, WrapWithColor(sponsorName, sponsorColor));

        if (candidateCount > 1)
        {
            var candidateSegment = ResolveCandidateSegment(candidateIndex, candidateCount);
            if (!string.IsNullOrWhiteSpace(candidateSegment))
            {
                result = ReplaceFirst(
                    result,
                    candidateSegment,
                    WrapWithColor(
                        candidateSegment,
                        ResolveCandidateTextColor(candidateIndex, candidateCount)
                    )
                );
            }
        }

        return result;
    }

    private static Color ResolveCandidateTextColor(int candidateIndex, int candidateCount)
    {
        var normalized =
            candidateCount <= 1 ? 0f : Mathf.Clamp01(candidateIndex / (float)(candidateCount - 1));
        return Color.Lerp(
            new Color(0.78f, 0.90f, 1f, 1f),
            new Color(0.95f, 0.98f, 1f, 1f),
            normalized
        );
    }

    private static string ResolveCandidateSegment(int candidateIndex, int candidateCount)
    {
        if (candidateCount <= 1)
            return string.Empty;

        var languageCode = PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        var label = LanguageCodeMatcher.IsChinese(languageCode) ? "候选" : "Candidate";
        return $"{label} {candidateIndex + 1}/{candidateCount}";
    }

    private static string WrapWithColor(string text, Color color)
    {
        var colorHex = ColorUtility.ToHtmlStringRGB(color);
        return $"<color=#{colorHex}>{text}</color>";
    }

    private static string ReplaceFirst(string source, string target, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return source;

        var index = source.IndexOf(target, StringComparison.Ordinal);
        if (index < 0)
            return source;

        return source[..index] + replacement + source[(index + target.Length)..];
    }

    private static string ReplaceLast(string source, string target, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return source;

        var index = source.LastIndexOf(target, StringComparison.Ordinal);
        if (index < 0)
            return source;

        return source[..index] + replacement + source[(index + target.Length)..];
    }

    private void ResolveSponsorTextStyle(string? sampleText)
    {
        var languageCode = PlayerPreferences.Data?.LanguageCode ?? string.Empty;
        var template = ResolveSponsorTemplateText(languageCode, sampleText);

        if (template == null)
            return;

        var templatePath = BuildTransformPath(template.transform);
        if (
            ReferenceEquals(_resolvedSponsorFont, template.font)
            && ReferenceEquals(_resolvedSponsorFontMaterial, template.fontSharedMaterial)
            && string.Equals(_resolvedSponsorFontSourcePath, templatePath, StringComparison.Ordinal)
        )
        {
            return;
        }

        _resolvedSponsorFont = template.font;
        _resolvedSponsorFontMaterial = template.fontSharedMaterial;
        _resolvedSponsorFontSourcePath = templatePath;

        if (!_resolvedSponsorFontLogged)
            _resolvedSponsorFontLogged = true;

        BppLog.Info(
            "ItemBoardOverlay",
            $"Resolved sponsor TMP font '{_resolvedSponsorFont?.name ?? "<null>"}' material='{_resolvedSponsorFontMaterial?.name ?? "<null>"}' source='{templatePath}' text='{template.text ?? string.Empty}'"
        );
    }

    private static TextMeshProUGUI? ResolveSponsorTemplateText(
        string languageCode,
        string? sampleText
    )
    {
        TextMeshProUGUI? settingsDockCandidate = null;
        TextMeshProUGUI? chineseCandidate = null;
        TextMeshProUGUI? fallback = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (candidate == null || candidate.font == null)
                continue;

            fallback ??= candidate;
            var path = BuildTransformPath(candidate.transform);
            if (
                settingsDockCandidate == null
                && path.IndexOf("BPP_SettingsDock", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                settingsDockCandidate = candidate;
            }

            if (
                chineseCandidate == null
                && ShouldPreferForLanguage(candidate, languageCode, sampleText)
            )
            {
                chineseCandidate = candidate;
            }
        }

        return chineseCandidate ?? settingsDockCandidate ?? fallback;
    }

    private static bool ShouldPreferForLanguage(
        TextMeshProUGUI candidate,
        string languageCode,
        string? sampleText
    )
    {
        if (!LanguageCodeMatcher.IsChinese(languageCode))
            return false;

        if (ContainsCjk(candidate.text))
            return true;

        if (!ContainsCjk(sampleText) || candidate.font == null)
            return false;

        return FontLooksCjkCapable(candidate.font);
    }

    private static bool FontLooksCjkCapable(TMP_FontAsset font)
    {
        var name = font.name ?? string.Empty;
        return name.IndexOf("Han", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Noto", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("CJK", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsCjk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var ch in text)
        {
            if (
                (ch >= 0x4E00 && ch <= 0x9FFF)
                || (ch >= 0x3400 && ch <= 0x4DBF)
                || (ch >= 0xF900 && ch <= 0xFAFF)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildTransformPath(Transform? transform)
    {
        if (transform == null)
            return "<unknown>";

        var segments = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            segments.Push(current.name);

        return string.Join("/", segments);
    }

    private static void HideHealthVisuals(
        Transform? healthTextTransform,
        Transform? carpetTransform
    )
    {
        if (healthTextTransform == null)
            return;

        healthTextTransform.gameObject.SetActive(false);

        var healthRoot = FindHealthContainer(healthTextTransform, carpetTransform);
        if (healthRoot != null)
            healthRoot.gameObject.SetActive(false);
    }

    private static Transform? FindHealthContainer(
        Transform healthTextTransform,
        Transform? carpetTransform
    )
    {
        Transform? fallback = healthTextTransform.parent;
        for (var current = healthTextTransform.parent; current != null; current = current.parent)
        {
            if (current == carpetTransform)
                break;

            if (current.name.IndexOf("health", StringComparison.OrdinalIgnoreCase) >= 0)
                return current;

            if (
                current != healthTextTransform.parent
                && current.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length > 1
            )
            {
                fallback = current;
            }
        }

        return fallback;
    }

    private static TMonster BuildSyntheticMonster(IReadOnlyList<ItemBoardItemSpec> items)
    {
        var instances = new List<TCardInstanceItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var spec = items[i];
            instances.Add(
                new TCardInstanceItem
                {
                    TemplateId = spec.TemplateId,
                    TemplateVersion = string.Empty,
                    InstanceId = $"bpp-itemboard-{i}",
                    Tier = spec.Tier,
                    SocketId = spec.SocketId ?? ResolveSyntheticSocket(i),
                    EnchantmentType = spec.EnchantmentType,
                    Attributes =
                        spec.Attributes != null
                            ? new Dictionary<ECardAttributeType, int>(spec.Attributes)
                            : new Dictionary<ECardAttributeType, int>(),
                }
            );
        }

        return new TMonster
        {
            Id = Guid.Empty,
            Version = string.Empty,
            InternalName = "item_board_template_set",
            Player = new TPlayer
            {
                Attributes = new Dictionary<EPlayerAttributeType, int>
                {
                    [EPlayerAttributeType.HealthMax] = 0,
                },
                Hand = new TPlayerInventory
                {
                    UnlockedSlots = (ushort)Mathf.Max(10, instances.Count),
                    Items = instances,
                },
            },
        };
    }

    private static EContainerSocketId ResolveSyntheticSocket(int index)
    {
        var clamped = Mathf.Clamp(index, 0, 9);
        return (EContainerSocketId)clamped;
    }
}
