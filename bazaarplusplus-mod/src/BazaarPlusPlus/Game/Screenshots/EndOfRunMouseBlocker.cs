#nullable enable
using TheBazaar.UI.EndOfRun;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunMouseBlocker
{
    private const string BlockerCanvasObjectName = "BPP_EndOfRunInputBlockerCanvas";
    private const string BlockerObjectName = "BPP_EndOfRunMouseBlocker";
    private const int BlockerCanvasSortingOrder = short.MaxValue;
    private EndOfRunScreenController? _owner;
    private GameObject? _blockerCanvasObject;
    private GameObject? _blockerObject;
    private EndOfRunInputCaptureSink? _inputSink;
    private bool _isAttached;

    public void Attach(EndOfRunScreenController screenController)
    {
        if (_owner != null && !ReferenceEquals(_owner, screenController))
            DestroyBlocker();

        if (_blockerCanvasObject == null || _blockerObject == null || _inputSink == null)
            CreateBlocker(screenController);

        if (_isAttached && ReferenceEquals(_owner, screenController))
            return;

        _owner = screenController;
        _blockerCanvasObject?.SetActive(true);
        _blockerObject?.SetActive(true);
        _inputSink?.CaptureFocus();
        _isAttached = true;
    }

    public void Detach()
    {
        if (!_isAttached)
            return;

        _inputSink?.ReleaseFocus();
        _blockerCanvasObject?.SetActive(false);
        _blockerObject?.SetActive(false);
        _isAttached = false;
        _owner = null;
    }

    public void Destroy()
    {
        DestroyBlocker();
    }

    private void CreateBlocker(EndOfRunScreenController screenController)
    {
        if (screenController == null)
            return;

        _blockerCanvasObject = new GameObject(
            BlockerCanvasObjectName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _blockerCanvasObject.layer = screenController.gameObject.layer;
        _blockerCanvasObject.transform.SetParent(
            screenController.transform,
            worldPositionStays: false
        );

        var blockerCanvas = _blockerCanvasObject.GetComponent<Canvas>();
        blockerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blockerCanvas.overrideSorting = true;
        blockerCanvas.sortingOrder = BlockerCanvasSortingOrder;

        var scaler = _blockerCanvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _blockerObject = new GameObject(
            BlockerObjectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(EndOfRunInputCaptureSink)
        );
        _blockerObject.layer = screenController.gameObject.layer;

        var blockerTransform = _blockerObject.GetComponent<RectTransform>();
        blockerTransform.SetParent(_blockerCanvasObject.transform, worldPositionStays: false);
        blockerTransform.anchorMin = Vector2.zero;
        blockerTransform.anchorMax = Vector2.one;
        blockerTransform.offsetMin = Vector2.zero;
        blockerTransform.offsetMax = Vector2.zero;
        blockerTransform.localScale = Vector3.one;

        var image = _blockerObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
        _inputSink = _blockerObject.GetComponent<EndOfRunInputCaptureSink>();
    }

    private void DestroyBlocker()
    {
        _inputSink?.ReleaseFocus();
        if (_blockerCanvasObject != null)
            Object.Destroy(_blockerCanvasObject);

        _blockerCanvasObject = null;
        _blockerObject = null;
        _inputSink = null;
        _isAttached = false;
        _owner = null;
    }
}
