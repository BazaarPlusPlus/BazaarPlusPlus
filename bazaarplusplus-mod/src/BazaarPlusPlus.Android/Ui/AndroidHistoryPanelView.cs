using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android twin of the desktop's <c>HistoryPanelView</c>: the same owned uGUI panel over the
/// IL2CPP proxies, laid out from Core's <see cref="HistoryPanelLayout"/> on a 1600x1000 canvas
/// matched at .5, and bound from Core's <see cref="HistoryPanelViewModel"/>, so the archive list,
/// the section tabs, the battle timeline, the pagers, the page label, the board rects with their
/// status, the status line and the replay action read and behave as the desktop's. Touch targets
/// grow to <see cref="HistoryPanelLayout.TouchTargetUnits"/> through
/// <see cref="HistoryPanelLayout.Touch"/>. What the desktop draws from native art (frames, hero
/// portraits, rank badges, trophies) renders flat here; the two BPP badge atlases ship in the
/// assembly. The Favorites tab, the filter strip, the delete action, the More dialog and the
/// native boards belong to #399: their rects are reserved and left empty.
/// </summary>
internal sealed partial class AndroidHistoryPanelView : IDisposable
{
    public const int SortingOrder = 29000;
    private const int Rows = 40;
    private static readonly Color Ink = new(0.96f, 0.89f, 0.73f, 1f);
    private static readonly Color Muted = new(0.69f, 0.63f, 0.53f, 1f);
    private static readonly Color ButtonFill = new(0.13f, 0.09f, 0.055f, 1f);
    private static readonly Color ButtonSelected = new(0.25f, 0.20f, 0.10f, 1f);
    private static readonly Color Backdrop = new(0.055f, 0.038f, 0.026f, 1f);
    private static readonly Color PanelFill = new(0.09f, 0.07f, 0.05f, 1f);

    private readonly TMP_FontAsset _font;
    private readonly ViewActions _actions;
    private readonly Dictionary<IntPtr, bool> _selection = new();
    private readonly Il2CppStructArray<Vector3> _corners = new(4);
    private GameObject? _root;
    private RectTransform? _layout;
    private HistoryPanelLayout _geometry;
    private HistoryPanelViewModel? _model;
    private AndroidHistoryBadges? _badges;
    private bool _visible;
    private bool _disposed;
    private int _layoutFrames = 2;
    private Vector2 _screenSize;
    private Rect _lastPlayerBounds;
    private Rect _lastOpponentBounds;

    public AndroidHistoryPanelView(TMP_FontAsset font, ViewActions actions)
    {
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The player board rect moved, in screen space; the board seam follows it.</summary>
    public event Action<Rect>? PlayerBoundsChanged;

    public event Action<Rect>? OpponentBoundsChanged;

    public bool IsCreated => _root != null;

    public int RootCount => _root != null ? 1 : 0;

    public void EnsureCreated()
    {
        if (_disposed || _root != null)
            return;
        _geometry = new HistoryPanelLayout(Screen.width, Screen.height);
        _badges = AndroidHistoryBadges.TryLoad();
        _root = new GameObject("BPP.HistoryPanel");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(
            HistoryPanelLayout.ReferenceWidth,
            HistoryPanelLayout.ReferenceHeight
        );
        scaler.matchWidthOrHeight = HistoryPanelLayout.Match;
        _root.AddComponent<GraphicRaycaster>();
        CreateLayout();
        _root.SetActive(_visible);
        UpdateContent();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.view_created"),
            ("screen_width", Screen.width),
            ("screen_height", Screen.height),
            ("canvas_height", _geometry.Height),
            ("badges", _badges != null)
        );
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        EnsureCreated();
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
        if (visible)
        {
            _layoutFrames = 2;
            UpdateContent();
        }
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

    public void SetBoardStatus(string playerMessage, string opponentMessage)
    {
        if (_playerStatus != null)
        {
            _playerStatus.text = playerMessage;
            _playerStatus.gameObject.SetActive(playerMessage.Length > 0);
        }
        if (_opponentStatus != null)
            _opponentStatus.text = opponentMessage;
    }

    /// <summary>Main thread, every frame while visible: re-reads the board rects after layout.</summary>
    public void Tick()
    {
        if (!_visible || _disposed)
            return;
        EnsureCreated();
        var size = new Vector2(Screen.width, Screen.height);
        if (_screenSize != size)
        {
            _screenSize = size;
            _layoutFrames = 2;
        }
        if (_layoutFrames <= 0 || _playerBoard == null)
            return;
        _layoutFrames--;
        Canvas.ForceUpdateCanvases();
        var bounds = WorldBounds(_playerBoard);
        if (bounds.width > 1 && bounds.height > 1 && bounds != _lastPlayerBounds)
        {
            _lastPlayerBounds = bounds;
            PlayerBoundsChanged?.Invoke(bounds);
        }
        if (_model?.ShowsBothBoards == true && _opponentBoard != null)
        {
            bounds = WorldBounds(_opponentBoard);
            if (bounds.width > 1 && bounds != _lastOpponentBounds)
            {
                _lastOpponentBounds = bounds;
                OpponentBoundsChanged?.Invoke(bounds);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        _badges?.Dispose();
        _badges = null;
    }

    private Rect WorldBounds(RectTransform rect)
    {
        rect.GetWorldCorners(_corners);
        return Rect.MinMaxRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
    }

    private static RectTransform Place(string name, Transform parent, HistoryRect rect) =>
        Place(name, parent, rect.X, rect.Y, rect.Width, rect.Height);

    private static RectTransform Place(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height
    )
    {
        var anchors = HistoryPanelLayout.Anchors(x, y, width, height);
        return AndroidUiKit.Rect(
            name,
            parent,
            new Vector2(anchors.MinX, anchors.MinY),
            new Vector2(anchors.MaxX, anchors.MaxY),
            Vector2.zero,
            Vector2.zero
        );
    }

    private static void Anchor(RectTransform rect, HistoryAnchors anchors)
    {
        rect.anchorMin = new Vector2(anchors.MinX, anchors.MinY);
        rect.anchorMax = new Vector2(anchors.MaxX, anchors.MaxY);
    }

    // A touch target keeps its authored top and grows down to the touch minimum.
    private HistoryRect Touch(HistoryRect rect) =>
        _geometry.Touch(rect, HistoryPanelLayout.TouchTargetUnits);

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
        var text = Place("Label", parent, x, y, width, height)
            .gameObject.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.text = label;
        text.fontSize = size;
        text.color = color ?? Ink;
        text.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = true;
        text.raycastTarget = false;
        return text;
    }

    private TextMeshProUGUI Text(
        Transform parent,
        string label,
        HistoryRect rect,
        int size,
        Color? color = null,
        bool center = false
    ) => Text(parent, label, rect.X, rect.Y, rect.Width, rect.Height, size, color, center);

    // MEMORY gotcha (desktop): NoWrap + Ellipsis at a scaled font can drop a whole TMP block;
    // every row label opts into wrapping, as the desktop's do.
    private static TextMeshProUGUI Wrapped(TextMeshProUGUI text)
    {
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private (Button Button, TextMeshProUGUI Label) Button(
        Transform parent,
        string control,
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
        var rect = Place("HistoryAction", parent, x, y, width, height);
        var fill = AndroidUiKit.Fill(rect, selected ? ButtonSelected : ButtonFill);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        AndroidUiActions.OnTap(button, control, click);
        var text = Text(rect, label, .04f, .06f, .92f, .88f, size, Ink, true);
        _selection[button.Pointer] = selected;
        return (button, text);
    }

    private (Button Button, TextMeshProUGUI Label) Button(
        Transform parent,
        string control,
        string label,
        HistoryRect rect,
        Action click,
        bool selected = false,
        int size = 17
    ) =>
        Button(
            parent,
            control,
            label,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            click,
            selected,
            size
        );

    private void SetButton(
        (Button Button, TextMeshProUGUI Label) control,
        string label,
        bool selected = false,
        bool enabled = true
    )
    {
        control.Label.text = label;
        control.Button.interactable = enabled;
        if (_selection.TryGetValue(control.Button.Pointer, out var was) && was == selected)
            return;
        _selection[control.Button.Pointer] = selected;
        var fill = control.Button.targetGraphic.TryCast<Image>();
        if (fill != null)
            fill.color = selected ? ButtonSelected : ButtonFill;
    }

    private static Image Badge(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height
    )
    {
        var image = Place(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.color = Color.clear;
        return image;
    }

    private static Color StatusColor(StatusSeverity severity) =>
        severity switch
        {
            StatusSeverity.Failure => new Color(0.90f, 0.40f, 0.31f, 1f),
            StatusSeverity.Success => new Color(0.48f, 0.76f, 0.42f, 1f),
            StatusSeverity.Confirm => Ink,
            _ => Muted,
        };

    /// <summary>What the view's controls call; each runs inside the main-thread scope.</summary>
    public sealed record ViewActions(
        Action Close,
        Action Replay,
        Action<int> SelectRun,
        Action<int> SelectBattle,
        Action<HistorySectionMode> Section,
        Action<int> PageArchive,
        Action<int> PageBattles
    );
}
