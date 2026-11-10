#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.Ranks;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

/// <summary>
/// The Desktop History panel, built from the Desktop UI kit: a rail at the left (title,
/// supporter line, section tabs, filters, the archive list and its pager) and the selected
/// battle at the right (the two boards between their owners' titles, the run's day strip and
/// the actions). uGUI layout groups place everything; the view owns no rect arithmetic.
/// </summary>
internal sealed partial class HistoryPanelView : IDisposable
{
    private const float RailWidth = 440f;
    private const float RowHeight = 64f;
    private const int Slots = 40;

    private static readonly Color Ink = InGameTheme.Ink;
    private static readonly Color Muted = InGameTheme.InkMuted;
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
        EHero Hero,
        Task<HeroPortraitLoadOutcome?> Load,
        int Generation
    )> _portraits = new();
    private readonly NativeRankBadgeProvider _rankBadges = new();
    private readonly NativeTrophyProvider _trophies = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private DesktopKit? _kit;
    private GameObject? _root;
    private RectTransform? _layout;
    private RectTransform? _preview;
    private RectTransform? _opponentPreview;
    private Rect _lastBounds;
    private Rect _lastOpponentBounds;
    private TextMeshProUGUI? _opponentStatus;
    private TextMeshProUGUI? _previewStatus;
    private NativeGameTypography.OwnedTextPreparation? _font;
    private BPPSupporterNativeAttributionRow? _supporters;
    private HistoryPanelViewModel? _model;
    private bool _boardsAvailable;
    private string _detailMessage = string.Empty;
    private bool _visible;
    private bool _disposed;
    private int _generation;
    private Action<int>? _pageArchive;
    private Action<int>? _pageBattles;
    public event Action<Rect>? PreviewContainerBoundsChanged;
    public event Action<Rect>? OpponentBoundsChanged;
    internal bool ShowsBothBoards => _model?.ShowsBothBoards == true;

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
        _kit = new DesktopKit(_font!);
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
            _more?.Root.SetActive(false);
            _heroMenu?.Root.SetActive(false);
            CloseNoteDialog();
        }
        else
            UpdateContent();
    }

    public bool IsTextInputFocused() =>
        (_accountInput != null && _accountInput.isFocused)
        || (_noteInput != null && _noteInput.isFocused);

    internal bool HasOpenModal =>
        _heroMenu?.Open == true || _more?.Open == true || _note?.Open == true;

    internal void DismissModal()
    {
        if (_note?.Open == true)
            CloseNoteDialog();
        else if (_heroMenu?.Open == true)
            _heroMenu.Root.SetActive(false);
        else if (_more?.Open == true)
            SetMoreVisible(false);
    }

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
        _detailMessage = message ?? string.Empty;
        if (_previewStatus != null)
        {
            _previewStatus.text = message ?? string.Empty;
            _previewStatus.gameObject.SetActive(visible && _boardsAvailable);
        }
        UpdateEmptyDetail();
    }

    internal void SetBoardsAvailable(bool available)
    {
        if (_boardsAvailable == available)
            return;
        _boardsAvailable = available;
        UpdateContent();
    }

    public void Tick()
    {
        if (!_visible || _disposed)
            return;
        EnsureCreated();
        if (_root == null)
            return;
        _kit!.Tick();
        // A rank badge or a trophy that arrives late goes on the rows that wait for it.
        if (_rankBadges.Tick() | _trophies.Tick())
            UpdateContent();
        for (var i = _portraits.Count - 1; i >= 0; i--)
        {
            var request = _portraits[i];
            if (!request.Load.IsCompleted)
                continue;
            if (
                request.Generation == _generation
                && request.Target != null
                && request.Load.Status == TaskStatus.RanToCompletion
                && request.Load.Result?.Sprite is { } sprite
            )
                DesktopKit.ShowPortrait(
                    request.Target,
                    sprite,
                    HeroPortraitFocus.For(request.Hero),
                    HeroPortraitFocus.Glow(request.Hero)
                );
            else if (request.Load.IsFaulted)
                _ = request.Load.Exception;
            _portraits.RemoveAt(i);
        }
        UpdateInputFocus();
        RevealSelectedDay();
        // The boards are drawn by the game into these two rects; layout moves them when the
        // section or the window changes.
        if (Bounds(_preview) is { } bounds && bounds != _lastBounds)
        {
            _lastBounds = bounds;
            PreviewContainerBoundsChanged?.Invoke(bounds);
        }
        if (
            ShowsBothBoards
            && Bounds(_opponentPreview) is { } opponent
            && opponent != _lastOpponentBounds
        )
        {
            _lastOpponentBounds = opponent;
            OpponentBoundsChanged?.Invoke(opponent);
        }
    }

    private Rect? Bounds(RectTransform? rect)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy)
            return null;
        rect.GetWorldCorners(_corners);
        var bounds = Rect.MinMaxRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
        return bounds.width > 1 && bounds.height > 1 ? bounds : null;
    }

    // Shows a hero in a portrait well; the well is empty until the portrait is loaded.
    private void ShowHero(Image portrait, string? heroId)
    {
        _portraits.RemoveAll(request => request.Target == portrait);
        DesktopKit.ShowPortrait(portrait, null);
        if (TheDragonsHeroIdentity.TryResolve(heroId, out var hero))
            _portraits.Add(
                (
                    portrait,
                    hero,
                    HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero),
                    _generation
                )
            );
    }

    private static HorizontalLayoutGroup Line(Transform parent, string name, float spacing) =>
        Line(DesktopKit.Rect(parent, name), spacing);

    private static HorizontalLayoutGroup Line(RectTransform rect, float spacing)
    {
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        return group;
    }

    private static VerticalLayoutGroup Column(RectTransform rect, float spacing)
    {
        var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = spacing;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return group;
    }

    private static T Flexible<T>(T target)
        where T : Component
    {
        var element = target.GetComponent<LayoutElement>();
        if (element == null)
            element = target.gameObject.AddComponent<LayoutElement>();
        element.flexibleWidth = 1;
        return target;
    }

    // A text of more than one line: it takes the height its width needs.
    private static TextMeshProUGUI Wrapped(TextMeshProUGUI text)
    {
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static Color OutcomeColor(HistoryOutcomeTone tone) =>
        tone switch
        {
            HistoryOutcomeTone.Won => InGameTheme.Success,
            HistoryOutcomeTone.Lost => InGameTheme.Failure,
            _ => Muted,
        };

    private static string Tinted(string text, Color color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

    public void Dispose()
    {
        _disposed = true;
        ReleaseInputFocus();
        _generation++;
        _portraits.Clear();
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        // Sprites and font assets belong to the shared native loaders.
    }
}
