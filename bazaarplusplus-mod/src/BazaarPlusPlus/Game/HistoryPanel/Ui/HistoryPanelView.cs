#nullable enable
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.Ranks;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Localization;
using TheBazaar.AppFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

// Owned native-art archive layout using the existing history selection state.
internal sealed partial class HistoryPanelView : IDisposable
{
    private const string Art = "Assets/TheBazaar/Art/UI/Buttons/";
    private static readonly string[] Addresses =
    [
        Art + "Btn_Rectangle/Btn_Rct_Frame_S_TUI.png",
        Art + "Btn_Rectangle/Btn_Rct_Brown_S_Center_Active_TUI.png",
        Art + "Btn_Rectangle/Btn_Rct_Blue_S_Center_Active_TUI.png",
        Art + "Btn_MatchHistory/Btn_MatchHistory_Run_Gold_Active_TUI.png",
        "Trophy_Bronze_TUI",
        "Trophy_Silver_TUI",
        "Trophy_Gold_TUI",
        "Trophy_Diamond_TUI",
    ];
    private static readonly Color Ink = new(0.96f, 0.89f, 0.73f);
    private static readonly Color Muted = new(0.69f, 0.63f, 0.53f);
    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _replay;
    private readonly Action<int> _selectRun;
    private readonly Action<int> _selectBattle;
    private readonly Action<HistorySectionMode> _section;
    private readonly Action<GhostBattleFilter> _ghostFilter;
    private readonly Action<string> _heroFilter;
    private readonly Action _ghostDay;
    private readonly List<(
        Image Target,
        Task<HeroPortraitLoadOutcome?> Load,
        int Generation
    )> _portraits = new();
    private GameObject? _root;
    private RectTransform? _layout;
    private RectTransform? _preview;
    private RectTransform? _opponentPreview;
    private Rect _lastOpponentBounds;
    private TextMeshProUGUI? _opponentStatus;
    private TextMeshProUGUI? _previewStatus;
    private NativeGameTypography.OwnedTextPreparation? _font;
    private Task<Sprite?[]>? _skinLoad;
    private Sprite?[]? _skin;
    private Texture2D? _resultAtlas;
    private Sprite? _winBadge;
    private Sprite? _lossBadge;
    private Texture2D? _stateAtlas;
    private Sprite[] _stateBadges = Array.Empty<Sprite>();
    private readonly NativeRankBadgeProvider _rankBadges = new();
    private HistoryPanelViewModel? _model;
    private bool _visible;
    private bool _disposed;
    private int _generation;
    private float _retryAt;
    private Rect _lastBounds;
    private int _layoutFrames = 2;
    private Vector2 _screenSize;
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<(Image Image, int Index, Color Tint)> _art = new();
    private readonly Dictionary<Button, bool> _buttonSelection = new();
    private Action<int>? _pageArchive;
    private Action<int>? _pageBattles;
    public event Action<Rect>? PreviewContainerBoundsChanged;
    public event Action<Rect>? OpponentBoundsChanged;
    internal bool ShowsBothBoards => _model?.SectionMode == HistorySectionMode.Runs;

    internal void SetOpponentStatus(string message)
    {
        if (_opponentStatus != null)
            _opponentStatus.text = message;
    }

    internal HistoryPanelView(
        Transform parent,
        Action close,
        Action replay,
        Action record,
        Action delete,
        Action checkHealth,
        Action<string?> submitAccountCode,
        Action toggleAccountLink,
        Action markAccountLinked,
        Action<int> selectRun,
        Action<int> selectBattle,
        Action<HistorySectionMode> section,
        Action<GhostBattleFilter> ghostFilter,
        Action<string> heroFilter,
        Action ghostDay
    )
    {
        _parent = parent;
        _close = close;
        _replay = replay;
        _record = record;
        _delete = delete;
        _checkHealth = checkHealth;
        _submitAccountCode = submitAccountCode;
        _toggleAccountLink = toggleAccountLink;
        _markAccountLinked = markAccountLinked;
        _selectRun = selectRun;
        _selectBattle = selectBattle;
        _section = section;
        _ghostFilter = ghostFilter;
        _heroFilter = heroFilter;
        _ghostDay = ghostDay;
    }

    internal void SetPaginationActions(Action<int> archive, Action<int> battles)
    {
        _pageArchive = archive;
        _pageBattles = battles;
    }

    public void EnsureCreated()
    {
        if (_disposed || _root != null)
            return;
        if (NativeGameTypography.PrepareOwnedText(out _font) != NativeGameTypography.Outcome.Ready)
            return;
        LoadResultBadges();
        _root = new GameObject(
            "HistoryPanelView",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _root.transform.SetParent(_parent, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = BppOverlaySorting.PanelUiToolkit;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 1000);
        scaler.matchWidthOrHeight = .5f;
        CreateLayout();
        _root.SetActive(_visible);
        UpdateContent();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        EnsureCreated();
        if (_root != null)
            _root.SetActive(visible);
        if (!visible)
        {
            ReleaseInputFocus();
            _accountCode = string.Empty;
            _accountInput?.SetTextWithoutNotify(string.Empty);
            _accountInput?.DeactivateInputField();
            _moreVisible = false;
            _moreRoot?.SetActive(false);
        }
        else
        {
            _layoutFrames = 2;
            UpdateContent();
        }
    }

    public bool IsTextInputFocused() => _accountInput != null && _accountInput.isFocused;

    public void Refresh(HistoryPanelViewModel model)
    {
        _model = model;
        if (_visible)
        {
            EnsureCreated();
            UpdateContent();
        }
    }

    public void SetPreviewStatus(string? message, bool visible)
    {
        if (_previewStatus != null)
        {
            _previewStatus.text = message ?? string.Empty;
            _previewStatus.gameObject.SetActive(visible);
        }
    }

    public void Tick()
    {
        if (!_visible || _disposed)
            return;
        EnsureCreated();
        if (_rankBadges.Tick())
            UpdateContent();
        if (
            (_skin == null || _skin.Any(sprite => sprite == null))
            && _skinLoad == null
            && Time.unscaledTime >= _retryAt
            && Services.TryGet<AssetLoader>(out var loader)
            && loader != null
        )
        {
            _retryAt = Time.unscaledTime + 5;
            _skinLoad = Task.WhenAll(
                Addresses.Select(address =>
                    NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
                )
            );
        }
        if (_skinLoad?.IsCompleted == true)
        {
            var changed = false;
            if (_skinLoad.Status == TaskStatus.RanToCompletion)
            {
                var loaded = _skinLoad.Result;
                changed =
                    _skin == null
                    || Enumerable.Range(0, loaded.Length).Any(i => loaded[i] != _skin[i]);
                _skin = loaded;
            }
            else
                _ = _skinLoad.Exception;
            _skinLoad = null;
            if (changed)
                RefreshSkin();
        }
        for (var i = _portraits.Count - 1; i >= 0; i--)
        {
            var request = _portraits[i];
            if (!request.Load.IsCompleted)
                continue;
            if (
                request.Generation == _generation
                && request.Target != null
                && request.Load.Status == TaskStatus.RanToCompletion
                && request.Load.Result?.Sprite != null
            )
            {
                request.Target.sprite = request.Load.Result.Sprite;
                request.Target.color = Color.white;
            }
            else if (request.Load.IsFaulted)
                _ = request.Load.Exception;
            _portraits.RemoveAt(i);
        }
        UpdateInputFocus();
        var size = new Vector2(Screen.width, Screen.height);
        if (_screenSize != size)
        {
            _screenSize = size;
            _layoutFrames = 2;
        }
        if (_layoutFrames <= 0 || _preview == null)
            return;
        _layoutFrames--;
        Canvas.ForceUpdateCanvases();
        _preview.GetWorldCorners(_corners);
        var bounds = Rect.MinMaxRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
        if (bounds.width > 1 && bounds.height > 1 && bounds != _lastBounds)
        {
            _lastBounds = bounds;
            PreviewContainerBoundsChanged?.Invoke(bounds);
        }
        if (ShowsBothBoards && _opponentPreview != null)
        {
            _opponentPreview.GetWorldCorners(_corners);
            bounds = Rect.MinMaxRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
            if (bounds != _lastOpponentBounds && bounds.width > 1)
            {
                _lastOpponentBounds = bounds;
                OpponentBoundsChanged?.Invoke(bounds);
            }
        }
    }

    private void RefreshSkin()
    {
        foreach (var art in _art)
        {
            art.Image.sprite = _skin?[art.Index];
            art.Image.color = art.Image.sprite != null ? art.Tint : Color.clear;
        }
        foreach (var entry in _buttonSelection)
            ApplyButtonSkin(entry.Key, entry.Value);
        _supporterAttribution?.SetSkin(_skin?[0], _skin?[1]);
        UpdateContent();
    }

    private void ApplyButtonSkin(Button button, bool selected)
    {
        var image = (Image)button.targetGraphic;
        image.sprite = _skin?[selected ? 2 : 1];
        image.color =
            image.sprite != null ? Color.white
            : selected ? new Color(.25f, .20f, .10f)
            : new Color(.13f, .09f, .055f);
    }

    private void SetButton(Button button, string label, bool selected = false, bool enabled = true)
    {
        button.GetComponentInChildren<TextMeshProUGUI>(true).text = label;
        button.interactable = enabled;
        _buttonSelection[button] = selected;
        ApplyButtonSkin(button, selected);
    }

    private void LoadResultBadges()
    {
        if (_resultAtlas == null)
        {
            var result = LoadBadgeAtlas("battle-result-badges.png", 2);
            _resultAtlas = result.Texture;
            _winBadge = result.Sprites[0];
            _lossBadge = result.Sprites[1];
        }
        if (_stateAtlas == null)
        {
            var states = LoadBadgeAtlas("run-state-badges.png", 3);
            _stateAtlas = states.Texture;
            _stateBadges = states.Sprites;
        }
    }

    private static (Texture2D Texture, Sprite[] Sprites) LoadBadgeAtlas(string name, int count)
    {
        using var stream = typeof(HistoryPanelView).Assembly.GetManifestResourceStream(
            $"BazaarPlusPlus.Resources.HistoryPanel.{name}"
        );
        if (stream == null)
            throw new InvalidOperationException($"History artwork is missing: {name}");
        using var bytes = new System.IO.MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        if (!texture.LoadImage(bytes.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidOperationException($"History artwork could not be decoded: {name}");
        }
        var width = texture.width / (float)count;
        var sprites = Enumerable
            .Range(0, count)
            .Select(index =>
                Sprite.Create(
                    texture,
                    new Rect(index * width, 0, width, texture.height),
                    new Vector2(.5f, .5f)
                )
            )
            .ToArray();
        return (texture, sprites);
    }

    private static string T(string english, string chinese) =>
        LocalizedTextHelpers.Resolve(new LocalizedTextSet(english, chinese));

    private static RectTransform CreateRect(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height
    )
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Anchor(rect, HistoryPanelLayout.Anchors(x, y, width, height));
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    // The single place top-down authoring becomes Unity's bottom-up anchors.
    private static void Anchor(RectTransform rect, HistoryAnchors anchors)
    {
        rect.anchorMin = new Vector2(anchors.MinX, anchors.MinY);
        rect.anchorMax = new Vector2(anchors.MaxX, anchors.MaxY);
    }

    private Image Layer(Transform parent, int spriteIndex, Color color)
    {
        var image = CreateRect("NativeArt", parent, 0, 0, 1, 1).gameObject.AddComponent<Image>();
        image.sprite = _skin?[spriteIndex];
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.color = image.sprite == null ? Color.clear : color;
        image.raycastTarget = false;
        _art.Add((image, spriteIndex, color));
        return image;
    }

    private void Panel(Transform parent, float x, float y, float width, float height)
    {
        var rect = CreateRect("NativePanel", parent, x, y, width, height);
        Layer(rect, 1, new Color(.48f, .43f, .38f));
        Layer(rect, 0, new Color(.72f, .62f, .45f));
    }

    private Button Button(
        Transform parent,
        string label,
        float x,
        float y,
        float width,
        float height,
        Action click,
        bool selected = false,
        int size = 17
    )
    {
        var rect = CreateRect("HistoryAction", parent, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = _skin?[selected ? 2 : 1];
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.color = image.sprite == null ? new Color(.18f, .14f, .1f) : Color.white;
        Layer(rect, 0, selected ? Ink : new Color(.65f, .55f, .39f));
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => click());
        Text(rect, label, .04f, .06f, .92f, .88f, size, Ink, true);
        _buttonSelection[button] = selected;
        return button;
    }

    private TextMeshProUGUI Text(
        Transform parent,
        string label,
        float x,
        float y,
        float width,
        float height,
        int size,
        Color? color = null,
        bool center = false
    )
    {
        var text = CreateRect("Label", parent, x, y, width, height)
            .gameObject.AddComponent<TextMeshProUGUI>();
        _font?.Apply(text);
        text.text = label;
        text.fontSize = size;
        text.color = color ?? Ink;
        text.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseInputFocus();
        _generation++;
        _portraits.Clear();
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        if (_winBadge != null)
            UnityEngine.Object.Destroy(_winBadge);
        if (_lossBadge != null)
            UnityEngine.Object.Destroy(_lossBadge);
        if (_resultAtlas != null)
            UnityEngine.Object.Destroy(_resultAtlas);
        foreach (var badge in _stateBadges)
            UnityEngine.Object.Destroy(badge);
        if (_stateAtlas != null)
            UnityEngine.Object.Destroy(_stateAtlas);
        _stateAtlas = null;
        _stateBadges = Array.Empty<Sprite>();
        _winBadge = _lossBadge = null;
        _resultAtlas = null;
        // Sprites and font assets belong to the shared native loaders.
    }
}
