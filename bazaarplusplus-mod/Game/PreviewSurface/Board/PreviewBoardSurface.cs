#pragma warning disable CS0436
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.MonsterPreview;
using UnityEngine;

namespace BazaarPlusPlus.Game.PreviewSurface;

internal sealed class PreviewBoardSurface : IPreviewBoardSurface
{
    private const int BoardSlotCount = 10;
    private const int DefaultSkillSlotCount = 3;
    private const float ContentYOffset = 0.2f;
    private const float BrandingBoardWidth = 0.5f;
    private const float MonsterInfoBoardGap = 0.04f;
    private const float MonsterInfoBoardDepthFactor = 0.5f;
    private const float MonsterInfoTextStripWidthInset = 0.08f;
    private const float MonsterInfoTextStripDepthFactor = 0.8f;
    private const int MonsterInfoBoardSortOrder = 20;
    private const int MonsterInfoTextStripSortOrder = 21;
    private const int MonsterInfoTextSortOrder = 30;
    private const float BoardCenterMarkerSize = 0.16f;
    private const float SlotMarkerSize = 0.08f;
    private const float CardCenterMarkerSize = 0.12f;
    private const float SkillBoardGap = 0.04f;
    private const float SkillSlotInset = 0.12f;
    private const float SkillCardScaleFactor = 0.8f;
    private static readonly Color ItemBoardFillColor = new Color(0.34f, 0.29f, 0.24f, 0.88f);
    private static readonly Color ItemBoardBorderColor = new Color(1f, 0.2f, 0.2f, 0.95f);
    private static readonly Color ItemBoardAccentColor = new Color(1f, 0.1f, 0.1f, 0.18f);
    private static readonly Color SkillBoardFillColor = new Color(0.24f, 0.29f, 0.33f, 0.16f);
    private static readonly Color SkillBoardBorderColor = new Color(0.25f, 0.7f, 1f, 0.95f);
    private static readonly Color BrandingBoardFillColor = new Color(0.17f, 0.20f, 0.23f, 0.62f);
    private static readonly Color BrandingBoardBorderColor = new Color(0.18f, 0.24f, 0.34f, 0.95f);
    private static readonly Color MonsterInfoBoardFillColor = new Color(0.16f, 0.22f, 0.18f, 0.72f);
    private static readonly Color MonsterInfoBoardBorderColor = new Color(0.16f, 0.20f, 0.18f, 1f);
    private static readonly Color MonsterInfoTextStripColor = new Color(0.03f, 0.04f, 0.04f, 0.98f);
    private static readonly Color MonsterHealthTextColor = new Color(
        143f / 255f,
        234f / 255f,
        49f / 255f,
        1f
    );
    private static readonly Color MonsterDividerTextColor = new Color(1f, 1f, 1f, 0.8f);
    private static readonly Color DebugItemMarkerColor = new Color(1f, 0f, 0f, 0.95f);
    private static readonly Color DebugSkillMarkerColor = new Color(0.25f, 0.75f, 1f, 0.95f);
    private static Font _boardTextFont;

    private readonly IPreviewCardSurface _factory;
    private readonly IPreviewCardSurface _skillFactory;
    private readonly GameObject _boardRoot;
    private readonly GameObject _visualRoot;
    private readonly GameObject _itemContentRoot;
    private readonly GameObject _skillContentRoot;
    private readonly List<GameObject> _boardSlots = new List<GameObject>();
    private readonly List<GameObject> _boardSlotMarkers = new List<GameObject>();
    private readonly List<GameObject> _cardAnchors = new List<GameObject>();
    private readonly List<GameObject> _cards = new List<GameObject>();
    private readonly List<GameObject> _cardCenterMarkers = new List<GameObject>();
    private readonly List<GameObject> _borderSegments = new List<GameObject>();
    private readonly List<int> _cardSizes = new List<int>();
    private readonly List<GameObject> _skillSlots = new List<GameObject>();
    private readonly List<GameObject> _skillSlotMarkers = new List<GameObject>();
    private readonly List<GameObject> _skillCards = new List<GameObject>();
    private int _activeSkillSlotCount = DefaultSkillSlotCount;

    private GameObject _boardPlate;
    private GameObject _boardFill;
    private GameObject _skillBoardFill;
    private GameObject _brandingBoardFill;
    private GameObject _monsterInfoBoardFill;
    private GameObject _monsterInfoTextStripFill;
    private readonly List<GameObject> _skillBoardBorders = new List<GameObject>();
    private readonly List<GameObject> _brandingBoardBorders = new List<GameObject>();
    private readonly List<GameObject> _monsterInfoBoardBorders = new List<GameObject>();
    private TextMesh _brandingText;
    private readonly List<TextMesh> _monsterInfoTexts = new List<TextMesh>();
    private GameObject _boardCenterMarker;
    private PreviewBoardPresentation _presentation = new PreviewBoardPresentation();
    private PreviewBoardDebugOptions _debugOptions = new PreviewBoardDebugOptions();
    private string _monsterHealthText = "?";
    private string _monsterXpText = "?";
    private string _monsterGoldText = "?";

    public bool IsAlive => _boardRoot != null;

    public Transform RootTransform => _boardRoot.transform;

    public PreviewBoardSurface(
        string name,
        IPreviewCardSurface factory,
        IPreviewCardSurface skillFactory
    )
    {
        _factory = factory;
        _skillFactory = skillFactory;
        _boardRoot = new GameObject(name);
        _visualRoot = new GameObject(name + "_Visual");
        _itemContentRoot = new GameObject(name + "_ItemContent");
        _skillContentRoot = new GameObject(name + "_SkillContent");
        _visualRoot.transform.SetParent(_boardRoot.transform, false);
        _itemContentRoot.transform.SetParent(_boardRoot.transform, false);
        _skillContentRoot.transform.SetParent(_boardRoot.transform, false);
        BuildVisuals();
        BuildBoardSlots();
        EnsureSkillSlots(DefaultSkillSlotCount);
        RefreshLayout();
        SetVisible(false);
        BppLog.Info("PreviewBoardSurface", $"Created board root='{_boardRoot.name}'");
    }

    public void SetPresentation(PreviewBoardPresentation presentation)
    {
        if (!IsAlive)
            return;

        _presentation = presentation ?? new PreviewBoardPresentation();
        RefreshLayout();
    }

    public void SetDebugOptions(PreviewBoardDebugOptions debugOptions)
    {
        if (!IsAlive)
            return;

        _debugOptions = debugOptions ?? new PreviewBoardDebugOptions();
        RefreshDebugVisuals();
    }

    public async Task RenderAsync(
        PreviewBoardModel model,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsAlive)
            return;

        model ??= new PreviewBoardModel();
        BppLog.Info(
            "PreviewBoardSurface",
            $"RenderAsync start root='{_boardRoot.name}' signature={model.Signature} items={model.ItemCards?.Count ?? 0} skills={model.SkillCards?.Count ?? 0}"
        );
        Clear();

        var metadata = model?.Metadata;
        _monsterHealthText = GetMetadataValue(metadata, "health", "hp");
        _monsterXpText = GetMetadataValue(metadata, "reward_xp", "xp");
        _monsterGoldText = GetMetadataValue(metadata, "reward_gold", "gold");
        RefreshMonsterInfoTexts();

        await RebuildItemsAsync(model.ItemCards, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            BppLog.Warn(
                "PreviewBoardSurface",
                $"RenderAsync cancelled after items root='{_boardRoot.name}' signature={model.Signature}"
            );
            Clear();
            return;
        }

        await RebuildSkillsAsync(model.SkillCards, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            BppLog.Warn(
                "PreviewBoardSurface",
                $"RenderAsync cancelled after skills root='{_boardRoot.name}' signature={model.Signature}"
            );
            Clear();
            return;
        }

        RefreshLayout();
        BppLog.Info(
            "PreviewBoardSurface",
            $"RenderAsync completed root='{_boardRoot.name}' signature={model.Signature} cards={_cards.Count} skillCards={_skillCards.Count}"
        );
    }

    public void SetVisible(bool visible)
    {
        if (_boardRoot != null && _boardRoot.activeSelf != visible)
        {
            _boardRoot.SetActive(visible);
            BppLog.Info(
                "PreviewBoardSurface",
                $"SetVisible root='{_boardRoot.name}' visible={visible}"
            );
        }
    }

    public void UpdateAnchor(Vector3 position, Quaternion rotation)
    {
        if (!IsAlive)
            return;

        _boardRoot.transform.SetPositionAndRotation(position, rotation);
        RefreshLayout();
    }

    public void Clear()
    {
        if (!IsAlive)
            return;

        var clearedItemCount = _cards.Count;
        var clearedSkillCount = _skillCards.Count;
        foreach (var cardObject in _cards)
        {
            if (cardObject != null)
                _factory.Destroy(cardObject);
        }
        _cards.Clear();
        _cardSizes.Clear();

        foreach (var marker in _cardCenterMarkers)
        {
            if (marker != null)
                UnityEngine.Object.Destroy(marker);
        }
        _cardCenterMarkers.Clear();

        foreach (var anchor in _cardAnchors)
        {
            if (anchor != null)
                UnityEngine.Object.Destroy(anchor);
        }
        _cardAnchors.Clear();

        foreach (var skillObject in _skillCards)
        {
            if (skillObject != null)
                _skillFactory.Destroy(skillObject);
        }
        _skillCards.Clear();
        BppLog.Debug(
            "PreviewBoardSurface",
            $"Clear root='{_boardRoot.name}' clearedItems={clearedItemCount} clearedSkills={clearedSkillCount}"
        );
    }

    public void Dispose()
    {
        Clear();
        if (_boardRoot != null)
            UnityEngine.Object.Destroy(_boardRoot);
    }

    private async Task RebuildItemsAsync(
        IReadOnlyList<PreviewCardSpec> cards,
        CancellationToken cancellationToken
    )
    {
        if (cards == null || cards.Count == 0)
            return;

        for (var index = 0; index < cards.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var anchor = new GameObject($"CardAnchor_{index}");
            anchor.transform.SetParent(_itemContentRoot.transform, false);
            _cardAnchors.Add(anchor);
            _cardSizes.Add(GetCardSize(cards[index]));

            var marker = CreateMarker($"CardCenter_{index}", CardCenterMarkerSize, false);
            marker.transform.SetParent(anchor.transform, false);
            _cardCenterMarkers.Add(marker);

            RefreshCardAnchor(index);

            var cardObject = await _factory.CreateAsync(cards[index], anchor.transform);
            if (cancellationToken.IsCancellationRequested)
            {
                if (cardObject != null)
                    _factory.Destroy(cardObject);
                return;
            }

            if (cardObject == null)
                continue;

            cardObject.transform.SetParent(anchor.transform, false);
            _cards.Add(cardObject);
            RefreshCard(index);
        }
    }

    private async Task RebuildSkillsAsync(
        IReadOnlyList<PreviewCardSpec> skillCards,
        CancellationToken cancellationToken
    )
    {
        _activeSkillSlotCount = DefaultSkillSlotCount;
        if (skillCards == null || skillCards.Count == 0)
            return;

        EnsureSkillSlots(skillCards.Count);
        _activeSkillSlotCount = Mathf.Max(DefaultSkillSlotCount, skillCards.Count);

        var count = Mathf.Min(skillCards.Count, _activeSkillSlotCount);
        var leadingEmpty = GetLeadingEmptySkillSlots(count);

        for (var index = 0; index < count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var slotIndex = leadingEmpty + index;
            if (slotIndex >= _skillSlots.Count)
                break;

            var slot = _skillSlots[slotIndex];
            var cardObject = await _skillFactory.CreateAsync(skillCards[index], slot.transform);
            if (cancellationToken.IsCancellationRequested)
            {
                if (cardObject != null)
                    _skillFactory.Destroy(cardObject);
                return;
            }

            if (cardObject == null)
                continue;

            cardObject.transform.SetParent(slot.transform, false);
            cardObject.transform.localPosition = Vector3.zero;
            cardObject.transform.localRotation = Quaternion.identity;
            cardObject.transform.localScale = _presentation.CardScale * SkillCardScaleFactor;
            _skillCards.Add(cardObject);
        }
    }

    private void RefreshLayout()
    {
        if (!IsAlive)
            return;

        RefreshVisuals();
        RefreshDebugVisuals();

        _itemContentRoot.transform.localPosition =
            _presentation.LocalOffset + new Vector3(0f, ContentYOffset, 0f);
        _itemContentRoot.transform.localRotation = Quaternion.identity;
        _itemContentRoot.transform.localScale = Vector3.one;

        _skillContentRoot.transform.localPosition =
            _presentation.LocalOffset
            + new Vector3(
                _presentation.BoardSize.x * 0.5f
                    + _presentation.SkillBoardWidth * 0.5f
                    + SkillBoardGap,
                ContentYOffset,
                0f
            );
        _skillContentRoot.transform.localRotation = Quaternion.identity;
        _skillContentRoot.transform.localScale = Vector3.one;
        SetActive(_skillContentRoot, _presentation.ShowSkillBoard);

        RefreshBoardSlots();
        RefreshSkillSlots();

        for (var index = 0; index < _cardAnchors.Count; index++)
        {
            RefreshCardAnchor(index);
            RefreshCard(index);
        }
    }

    private void BuildVisuals()
    {
        _boardPlate = CreatePrimitive("BoardPlate", false, keepCollider: true);
        _boardPlate.layer = LayerMask.NameToLayer("Input");
        _boardPlate.AddComponent<PreviewBoardSurfaceMarker>();
        if (_boardPlate.TryGetComponent<Renderer>(out var boardPlateRenderer))
            boardPlateRenderer.enabled = false;
        _boardPlate.transform.SetParent(_visualRoot.transform, false);

        _boardFill = CreatePrimitive("BoardFill", false);
        _boardFill.transform.SetParent(_visualRoot.transform, false);

        _skillBoardFill = CreatePrimitive("SkillBoardFill", true);
        _skillBoardFill.transform.SetParent(_visualRoot.transform, false);

        _brandingBoardFill = CreatePrimitive("BrandingBoardFill", true);
        _brandingBoardFill.transform.SetParent(_visualRoot.transform, false);

        _monsterInfoBoardFill = CreatePrimitive("MonsterInfoBoardFill", true);
        _monsterInfoBoardFill.transform.SetParent(_visualRoot.transform, false);
        SetRendererSorting(_monsterInfoBoardFill, MonsterInfoBoardSortOrder);

        _monsterInfoTextStripFill = CreatePrimitive("MonsterInfoTextStripFill", true);
        _monsterInfoTextStripFill.transform.SetParent(_visualRoot.transform, false);
        SetRendererSorting(_monsterInfoTextStripFill, MonsterInfoTextStripSortOrder);

        _boardCenterMarker = CreateMarker("BoardCenter", BoardCenterMarkerSize, false);
        _boardCenterMarker.transform.SetParent(_visualRoot.transform, false);

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"BoardBorder_{index}", false);
            border.transform.SetParent(_visualRoot.transform, false);
            _borderSegments.Add(border);
        }

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"SkillBoardBorder_{index}", true);
            border.transform.SetParent(_visualRoot.transform, false);
            _skillBoardBorders.Add(border);
        }

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"BrandingBoardBorder_{index}", true);
            border.transform.SetParent(_visualRoot.transform, false);
            _brandingBoardBorders.Add(border);
        }

        for (var index = 0; index < 4; index++)
        {
            var border = CreatePrimitive($"MonsterInfoBoardBorder_{index}", true);
            border.transform.SetParent(_visualRoot.transform, false);
            _monsterInfoBoardBorders.Add(border);
        }

        var brandingTextObject = new GameObject("BrandingText");
        brandingTextObject.transform.SetParent(_visualRoot.transform, false);
        _brandingText = brandingTextObject.AddComponent<TextMesh>();
        _brandingText.text = "BazaarPlusPlus";
        _brandingText.anchor = TextAnchor.MiddleCenter;
        _brandingText.alignment = TextAlignment.Center;
        _brandingText.characterSize = 0.24f;
        _brandingText.fontSize = 64;
        _brandingText.font = GetBoardTextFont();
        _brandingText.color = new Color(0.82f, 0.93f, 1f, 1f);
        SetRendererSorting(_brandingText.gameObject, MonsterInfoTextSortOrder);

        CreateMonsterInfoText("MonsterInfoHealth", 0.22f, 80);
        CreateMonsterInfoText("MonsterInfoDivider", 0.22f, 72);
        CreateMonsterInfoText("MonsterInfoRewards", 0.17f, 60);
        RefreshMonsterInfoTexts();
    }

    private void BuildBoardSlots()
    {
        for (var index = 0; index < BoardSlotCount; index++)
        {
            var slot = new GameObject($"BoardSlot_{index}");
            slot.transform.SetParent(_itemContentRoot.transform, false);
            _boardSlots.Add(slot);

            var marker = CreateMarker($"BoardSlotMarker_{index}", SlotMarkerSize, false);
            marker.transform.SetParent(slot.transform, false);
            _boardSlotMarkers.Add(marker);
        }
    }

    private void EnsureSkillSlots(int requestedCount)
    {
        var targetCount = Mathf.Max(DefaultSkillSlotCount, requestedCount);
        for (var index = _skillSlots.Count; index < targetCount; index++)
        {
            var slot = new GameObject($"SkillSlot_{index}");
            slot.transform.SetParent(_skillContentRoot.transform, false);
            _skillSlots.Add(slot);

            var marker = CreatePrimitive($"SkillSlotMarker_{index}", true);
            marker.transform.SetParent(slot.transform, false);
            _skillSlotMarkers.Add(marker);
        }
    }

    private void RefreshBoardSlots()
    {
        for (var index = 0; index < _boardSlots.Count; index++)
        {
            var slot = _boardSlots[index];
            if (slot == null)
                continue;

            slot.transform.localPosition = new Vector3(GetBoardSlotCenterX(index), 0f, 0f);
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;
        }
    }

    private void RefreshSkillSlots()
    {
        for (var index = 0; index < _skillSlots.Count; index++)
        {
            var slot = _skillSlots[index];
            if (slot == null)
                continue;

            slot.transform.localPosition = new Vector3(0f, 0f, GetSkillSlotCenterZ(index));
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;
            SetActive(slot, index < _activeSkillSlotCount);

            if (index < _skillSlotMarkers.Count && _skillSlotMarkers[index] != null)
            {
                _skillSlotMarkers[index].transform.localPosition = Vector3.zero;
                _skillSlotMarkers[index].transform.localRotation = Quaternion.identity;
                _skillSlotMarkers[index].transform.localScale = new Vector3(
                    Mathf.Max(0.01f, _presentation.SkillBoardWidth - SkillSlotInset * 2f),
                    0.03f,
                    Mathf.Max(0.01f, GetSkillSlotDepth() - SkillSlotInset)
                );
                SetActive(_skillSlotMarkers[index], index < _activeSkillSlotCount);
            }
        }
    }

    private void RefreshCardAnchor(int index)
    {
        if (index < 0 || index >= _cardAnchors.Count)
            return;

        var anchor = _cardAnchors[index];
        if (anchor == null)
            return;

        var span = Mathf.Clamp(GetCardSize(index), 1, 3);
        var startSlot = GetCardStartSlot(index);
        var endSlot = Mathf.Min(BoardSlotCount - 1, startSlot + span - 1);
        var centerX = (GetBoardSlotCenterX(startSlot) + GetBoardSlotCenterX(endSlot)) * 0.5f;
        var spacing = _presentation.CardSpacing;

        anchor.transform.localPosition = new Vector3(
            centerX * spacing.x,
            spacing.y * index,
            spacing.z * index
        );
        anchor.transform.localRotation = Quaternion.identity;
        anchor.transform.localScale = Vector3.one;
    }

    private void RefreshCard(int index)
    {
        if (index < 0 || index >= _cards.Count)
            return;

        var cardObject = _cards[index];
        if (cardObject == null)
            return;

        cardObject.transform.localPosition = Vector3.zero;
        cardObject.transform.localRotation = Quaternion.identity;
        cardObject.transform.localScale = _presentation.CardScale;
    }

    private void RefreshVisuals()
    {
        if (_visualRoot == null || _boardPlate == null || _borderSegments.Count < 4)
            return;

        var size = _presentation.BoardSize;
        var boardWidth = Mathf.Max(0.01f, size.x);
        var skillBoardWidth = Mathf.Max(0.01f, _presentation.SkillBoardWidth);
        var halfWidth = boardWidth * 0.5f;
        var halfDepth = size.y * 0.5f;
        var boardThickness = Mathf.Max(0.01f, _presentation.BoardThickness);
        var borderThickness = Mathf.Max(0.01f, _presentation.BorderThickness);
        var borderHeight = Mathf.Max(boardThickness, _presentation.BorderHeight);
        var skillBoardCenterX = halfWidth + SkillBoardGap + skillBoardWidth * 0.5f;
        var brandingBoardCenterX =
            halfWidth + SkillBoardGap * 2f + skillBoardWidth + BrandingBoardWidth * 0.5f;
        var monsterInfoBoardWidth = skillBoardWidth + SkillBoardGap + BrandingBoardWidth;
        var monsterInfoBoardDepth = Mathf.Max(0.01f, skillBoardWidth * MonsterInfoBoardDepthFactor);
        var monsterInfoBoardCenterX = halfWidth + SkillBoardGap + monsterInfoBoardWidth * 0.5f;
        var monsterInfoBoardCenterZ =
            halfDepth + MonsterInfoBoardGap + monsterInfoBoardDepth * 0.5f;
        var monsterInfoTextStripWidth = Mathf.Max(
            0.01f,
            monsterInfoBoardWidth - MonsterInfoTextStripWidthInset * 2f
        );
        var monsterInfoTextStripDepth = Mathf.Max(
            0.01f,
            monsterInfoBoardDepth * MonsterInfoTextStripDepthFactor
        );

        _visualRoot.transform.localPosition = Vector3.zero;
        _visualRoot.transform.localRotation = Quaternion.identity;
        _visualRoot.transform.localScale = Vector3.one;

        var combinedDepth = size.y;
        var combinedCenterZ = 0f;

        _boardPlate.transform.localPosition = new Vector3(0f, 0f, combinedCenterZ);
        _boardPlate.transform.localRotation = Quaternion.identity;
        _boardPlate.transform.localScale = new Vector3(boardWidth, boardThickness, combinedDepth);

        if (_boardFill != null)
        {
            SetActive(_boardFill, _presentation.ShowItemBoardFill);
            _boardFill.transform.localPosition = new Vector3(
                0f,
                boardThickness * 0.2f,
                combinedCenterZ
            );
            _boardFill.transform.localRotation = Quaternion.identity;
            _boardFill.transform.localScale = new Vector3(
                Mathf.Max(0.01f, boardWidth - borderThickness * 0.5f),
                Mathf.Max(0.01f, boardThickness * 0.35f),
                Mathf.Max(0.01f, combinedDepth - borderThickness * 0.5f)
            );
        }

        if (_skillBoardFill != null)
        {
            SetActive(_skillBoardFill, _presentation.ShowSkillBoard);
            _skillBoardFill.transform.localPosition = new Vector3(
                skillBoardCenterX,
                boardThickness * 0.2f,
                combinedCenterZ
            );
            _skillBoardFill.transform.localRotation = Quaternion.identity;
            _skillBoardFill.transform.localScale = new Vector3(
                skillBoardWidth,
                Mathf.Max(0.01f, boardThickness * 0.35f),
                combinedDepth
            );
        }

        if (_brandingBoardFill != null)
        {
            SetActive(_brandingBoardFill, _presentation.ShowBrandingBoard);
            _brandingBoardFill.transform.localPosition = new Vector3(
                brandingBoardCenterX,
                boardThickness * 0.2f,
                combinedCenterZ
            );
            _brandingBoardFill.transform.localRotation = Quaternion.identity;
            _brandingBoardFill.transform.localScale = new Vector3(
                BrandingBoardWidth,
                Mathf.Max(0.01f, boardThickness * 0.35f),
                combinedDepth
            );
        }

        if (_monsterInfoBoardFill != null)
        {
            SetActive(_monsterInfoBoardFill, _presentation.ShowMonsterInfoBoard);
            _monsterInfoBoardFill.transform.localPosition = new Vector3(
                monsterInfoBoardCenterX,
                boardThickness * 0.2f,
                monsterInfoBoardCenterZ
            );
            _monsterInfoBoardFill.transform.localRotation = Quaternion.identity;
            _monsterInfoBoardFill.transform.localScale = new Vector3(
                monsterInfoBoardWidth,
                Mathf.Max(0.01f, boardThickness * 0.35f),
                monsterInfoBoardDepth
            );
        }

        if (_monsterInfoTextStripFill != null)
        {
            SetActive(_monsterInfoTextStripFill, _presentation.ShowMonsterInfoBoard);
            _monsterInfoTextStripFill.transform.localPosition = new Vector3(
                monsterInfoBoardCenterX,
                boardThickness * 0.3f,
                monsterInfoBoardCenterZ
            );
            _monsterInfoTextStripFill.transform.localRotation = Quaternion.identity;
            _monsterInfoTextStripFill.transform.localScale = new Vector3(
                monsterInfoTextStripWidth,
                Mathf.Max(0.01f, boardThickness * 0.45f),
                monsterInfoTextStripDepth
            );
        }

        if (_boardCenterMarker != null)
        {
            _boardCenterMarker.transform.localPosition = new Vector3(
                0f,
                borderHeight + BoardCenterMarkerSize * 0.5f,
                0f
            );
            _boardCenterMarker.transform.localRotation = Quaternion.identity;
            _boardCenterMarker.transform.localScale = Vector3.one * BoardCenterMarkerSize;
        }

        UpdateBorder(
            _borderSegments[0],
            new Vector3(0f, borderHeight * 0.5f, -halfDepth),
            new Vector3(boardWidth + borderThickness, borderHeight, borderThickness)
        );
        UpdateBorder(
            _borderSegments[1],
            new Vector3(0f, borderHeight * 0.5f, halfDepth),
            new Vector3(boardWidth + borderThickness, borderHeight, borderThickness)
        );
        UpdateBorder(
            _borderSegments[2],
            new Vector3(-halfWidth, borderHeight * 0.5f, combinedCenterZ),
            new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
        );
        UpdateBorder(
            _borderSegments[3],
            new Vector3(halfWidth, borderHeight * 0.5f, combinedCenterZ),
            new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
        );

        if (_skillBoardBorders.Count >= 4)
        {
            var skillHalfWidth = skillBoardWidth * 0.5f;
            for (var index = 0; index < _skillBoardBorders.Count; index++)
                SetActive(_skillBoardBorders[index], _presentation.ShowSkillBoard);
            UpdateBorder(
                _skillBoardBorders[0],
                new Vector3(skillBoardCenterX, borderHeight * 0.5f, -halfDepth),
                new Vector3(skillBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _skillBoardBorders[1],
                new Vector3(skillBoardCenterX, borderHeight * 0.5f, halfDepth),
                new Vector3(skillBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _skillBoardBorders[2],
                new Vector3(
                    skillBoardCenterX - skillHalfWidth,
                    borderHeight * 0.5f,
                    combinedCenterZ
                ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
            UpdateBorder(
                _skillBoardBorders[3],
                new Vector3(
                    skillBoardCenterX + skillHalfWidth,
                    borderHeight * 0.5f,
                    combinedCenterZ
                ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
        }

        if (_brandingBoardBorders.Count >= 4)
        {
            var brandingHalfWidth = BrandingBoardWidth * 0.5f;
            for (var index = 0; index < _brandingBoardBorders.Count; index++)
                SetActive(_brandingBoardBorders[index], _presentation.ShowBrandingBoard);
            UpdateBorder(
                _brandingBoardBorders[0],
                new Vector3(brandingBoardCenterX, borderHeight * 0.5f, -halfDepth),
                new Vector3(BrandingBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _brandingBoardBorders[1],
                new Vector3(brandingBoardCenterX, borderHeight * 0.5f, halfDepth),
                new Vector3(BrandingBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _brandingBoardBorders[2],
                new Vector3(
                    brandingBoardCenterX - brandingHalfWidth,
                    borderHeight * 0.5f,
                    combinedCenterZ
                ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
            UpdateBorder(
                _brandingBoardBorders[3],
                new Vector3(
                    brandingBoardCenterX + brandingHalfWidth,
                    borderHeight * 0.5f,
                    combinedCenterZ
                ),
                new Vector3(borderThickness, borderHeight, combinedDepth + borderThickness)
            );
        }

        if (_monsterInfoBoardBorders.Count >= 4)
        {
            var monsterInfoHalfWidth = monsterInfoBoardWidth * 0.5f;
            var monsterInfoHalfDepth = monsterInfoBoardDepth * 0.5f;
            for (var index = 0; index < _monsterInfoBoardBorders.Count; index++)
                SetActive(_monsterInfoBoardBorders[index], _presentation.ShowMonsterInfoBoard);
            UpdateBorder(
                _monsterInfoBoardBorders[0],
                new Vector3(
                    monsterInfoBoardCenterX,
                    borderHeight * 0.5f,
                    monsterInfoBoardCenterZ - monsterInfoHalfDepth
                ),
                new Vector3(monsterInfoBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _monsterInfoBoardBorders[1],
                new Vector3(
                    monsterInfoBoardCenterX,
                    borderHeight * 0.5f,
                    monsterInfoBoardCenterZ + monsterInfoHalfDepth
                ),
                new Vector3(monsterInfoBoardWidth + borderThickness, borderHeight, borderThickness)
            );
            UpdateBorder(
                _monsterInfoBoardBorders[2],
                new Vector3(
                    monsterInfoBoardCenterX - monsterInfoHalfWidth,
                    borderHeight * 0.5f,
                    monsterInfoBoardCenterZ
                ),
                new Vector3(borderThickness, borderHeight, monsterInfoBoardDepth + borderThickness)
            );
            UpdateBorder(
                _monsterInfoBoardBorders[3],
                new Vector3(
                    monsterInfoBoardCenterX + monsterInfoHalfWidth,
                    borderHeight * 0.5f,
                    monsterInfoBoardCenterZ
                ),
                new Vector3(borderThickness, borderHeight, monsterInfoBoardDepth + borderThickness)
            );
        }

        if (_brandingText != null)
        {
            SetActive(_brandingText.gameObject, _presentation.ShowBrandingBoard);
            _brandingText.transform.localPosition = new Vector3(
                brandingBoardCenterX,
                borderHeight + 0.01f,
                0f
            );
            _brandingText.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);
            _brandingText.transform.localScale = Vector3.one * 0.2f;
        }

        RefreshMonsterInfoTextLayout(
            monsterInfoBoardCenterX,
            monsterInfoBoardCenterZ,
            monsterInfoBoardWidth,
            borderHeight
        );
    }

    private void RefreshDebugVisuals()
    {
        var debugEnabled = IsDebugVisualEnabled();

        SetActive(_boardCenterMarker, debugEnabled && _debugOptions.ShowAnchorPoint);
        SetActive(_brandingText?.gameObject, _presentation.ShowBrandingBoard);

        for (var index = 0; index < _boardSlotMarkers.Count; index++)
            SetActive(_boardSlotMarkers[index], debugEnabled && _debugOptions.ShowItemSlots);

        for (var index = 0; index < _skillSlotMarkers.Count; index++)
            SetActive(_skillSlotMarkers[index], debugEnabled && _debugOptions.ShowSkillSlots);

        for (var index = 0; index < _cardCenterMarkers.Count; index++)
            SetActive(_cardCenterMarkers[index], debugEnabled && _debugOptions.ShowCardBounds);
    }

    private float GetBoardSlotCenterX(int slotIndex)
    {
        var boardWidth = Mathf.Max(0.01f, _presentation.BoardSize.x);
        var slotWidth = boardWidth / BoardSlotCount;
        return -boardWidth * 0.5f + slotWidth * (slotIndex + 0.5f);
    }

    private float GetSkillSlotCenterZ(int slotIndex)
    {
        var slotDepth = GetSkillSlotDepth();
        return _presentation.BoardSize.y * 0.5f - slotDepth * (slotIndex + 0.5f);
    }

    private float GetSkillSlotDepth()
    {
        var slotCount = Mathf.Max(DefaultSkillSlotCount, _activeSkillSlotCount);
        return Mathf.Max(0.01f, _presentation.BoardSize.y / slotCount);
    }

    private int GetLeadingEmptySkillSlots(int filledCount)
    {
        var slotCount = Mathf.Max(DefaultSkillSlotCount, _activeSkillSlotCount);
        var freeSlots = Mathf.Max(0, slotCount - filledCount);
        return freeSlots / 2;
    }

    private int GetCardStartSlot(int cardIndex)
    {
        var slot = GetLeadingEmptySlots();
        for (var index = 0; index < cardIndex; index++)
            slot += Mathf.Clamp(GetCardSize(index), 1, 3);

        var span = Mathf.Clamp(GetCardSize(cardIndex), 1, 3);
        return Mathf.Clamp(slot, 0, Mathf.Max(0, BoardSlotCount - span));
    }

    private int GetLeadingEmptySlots()
    {
        var occupiedSlots = 0;
        for (var index = 0; index < _cardSizes.Count; index++)
            occupiedSlots += Mathf.Clamp(_cardSizes[index], 1, 3);

        var freeSlots = Mathf.Max(0, BoardSlotCount - occupiedSlots);
        return freeSlots / 2;
    }

    private int GetCardSize(int index)
    {
        if (index < 0 || index >= _cardSizes.Count)
            return 1;

        return Mathf.Clamp(_cardSizes[index], 1, 3);
    }

    private static int GetCardSize(PreviewCardSpec spec)
    {
        return Mathf.Clamp(spec?.Size ?? 1, 1, 3);
    }

    private static void UpdateBorder(GameObject border, Vector3 position, Vector3 scale)
    {
        if (border == null)
            return;

        border.transform.localPosition = position;
        border.transform.localRotation = Quaternion.identity;
        border.transform.localScale = scale;
    }

    private TextMesh CreateMonsterInfoText(string name, float characterSize, int fontSize)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(_visualRoot.transform, false);
        var text = textObject.AddComponent<TextMesh>();
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.characterSize = characterSize;
        text.fontSize = fontSize;
        text.font = GetBoardTextFont();
        text.richText = true;
        text.color = new Color(0.90f, 0.98f, 0.92f, 1f);
        SetRendererSorting(textObject, MonsterInfoTextSortOrder);
        _monsterInfoTexts.Add(text);
        return text;
    }

    private void RefreshMonsterInfoTexts()
    {
        if (_monsterInfoTexts.Count > 0 && _monsterInfoTexts[0] != null)
        {
            _monsterInfoTexts[0].text = _monsterHealthText;
            _monsterInfoTexts[0].color = MonsterHealthTextColor;
        }

        if (_monsterInfoTexts.Count > 1 && _monsterInfoTexts[1] != null)
        {
            _monsterInfoTexts[1].text = "│";
            _monsterInfoTexts[1].color = MonsterDividerTextColor;
            _monsterInfoTexts[1].fontStyle = FontStyle.Normal;
        }

        if (_monsterInfoTexts.Count > 2 && _monsterInfoTexts[2] != null)
        {
            _monsterInfoTexts[2].text =
                $"<color=#63CEEC>{_monsterXpText}</color> / <color=#FFCC1B>{_monsterGoldText}</color>";
            _monsterInfoTexts[2].color = Color.white;
            _monsterInfoTexts[2].fontStyle = FontStyle.Bold;
        }
    }

    private void RefreshMonsterInfoTextLayout(
        float boardCenterX,
        float boardCenterZ,
        float boardWidth,
        float borderHeight
    )
    {
        if (_monsterInfoTexts.Count == 0)
            return;

        for (var index = 0; index < _monsterInfoTexts.Count; index++)
        {
            if (_monsterInfoTexts[index] != null)
                SetActive(_monsterInfoTexts[index].gameObject, _presentation.ShowMonsterInfoBoard);
        }

        if (!_presentation.ShowMonsterInfoBoard)
            return;

        var dividerX = boardCenterX + boardWidth * 0.02f;
        var healthRightX = dividerX - boardWidth * 0.04f;
        var rewardsX = dividerX + boardWidth * 0.15f;

        for (var index = 0; index < _monsterInfoTexts.Count; index++)
        {
            var text = _monsterInfoTexts[index];
            if (text == null)
                continue;

            float x = index switch
            {
                0 => healthRightX,
                1 => dividerX,
                _ => rewardsX,
            };
            text.anchor = index switch
            {
                0 => TextAnchor.MiddleRight,
                1 => TextAnchor.MiddleCenter,
                _ => TextAnchor.MiddleCenter,
            };
            text.alignment = index switch
            {
                0 => TextAlignment.Right,
                1 => TextAlignment.Center,
                _ => TextAlignment.Center,
            };
            text.transform.localPosition = new Vector3(x, borderHeight + 0.01f, boardCenterZ);
            text.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Vector3 textScale = index switch
            {
                0 => Vector3.one * 0.21f,
                1 => Vector3.one * 0.24f,
                _ => Vector3.one * 0.22f,
            };
            text.transform.localScale = textScale;
        }
    }

    private static string GetMetadataValue(
        IReadOnlyDictionary<string, string> metadata,
        params string[] keys
    )
    {
        if (metadata == null || keys == null)
            return "?";

        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "?";
    }

    private static Font GetBoardTextFont()
    {
        _boardTextFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _boardTextFont;
    }

    private static void SetRendererSorting(GameObject target, int sortingOrder)
    {
        if (target != null && target.TryGetComponent<Renderer>(out var renderer))
            renderer.sortingOrder = sortingOrder;
    }

    private bool IsDebugVisualEnabled()
    {
        return _presentation.DebugEnabled && _debugOptions.Enabled;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static GameObject CreatePrimitive(string name, bool isSkill, bool keepCollider = false)
    {
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        primitive.name = name;

        var collider = primitive.GetComponent<Collider>();
        if (collider != null && !keepCollider)
            UnityEngine.Object.Destroy(collider);

        ApplyBoardMaterial(primitive, name.Contains("Border"), isSkill);
        return primitive;
    }

    private static GameObject CreateMarker(string name, float size, bool isSkill)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;

        var collider = marker.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);

        ApplyDebugMarkerMaterial(marker, isSkill);
        marker.transform.localScale = Vector3.one * size;
        return marker;
    }

    private static void ApplyBoardMaterial(GameObject target, bool isBorder, bool isSkill)
    {
        if (!target.TryGetComponent<Renderer>(out var renderer))
            return;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        var material = new Material(shader);
        var isFill = target.name.Contains("Fill");
        var isBranding = target.name.Contains("Branding");
        var isMonsterInfoTextStrip = target.name.Contains("MonsterInfoTextStrip");
        material.color =
            isBranding ? (isBorder ? BrandingBoardBorderColor : BrandingBoardFillColor)
            : isMonsterInfoTextStrip ? MonsterInfoTextStripColor
            : target.name.Contains("MonsterInfo")
                ? (isBorder ? MonsterInfoBoardBorderColor : MonsterInfoBoardFillColor)
            : isSkill ? (isBorder ? SkillBoardBorderColor : SkillBoardFillColor)
            : isFill ? ItemBoardFillColor
            : (isBorder ? ItemBoardBorderColor : ItemBoardAccentColor);
        renderer.sharedMaterial = material;
    }

    private static void ApplyDebugMarkerMaterial(GameObject target, bool isSkill)
    {
        if (!target.TryGetComponent<Renderer>(out var renderer))
            return;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        var material = new Material(shader);
        material.color = isSkill ? DebugSkillMarkerColor : DebugItemMarkerColor;
        renderer.sharedMaterial = material;
    }
}
