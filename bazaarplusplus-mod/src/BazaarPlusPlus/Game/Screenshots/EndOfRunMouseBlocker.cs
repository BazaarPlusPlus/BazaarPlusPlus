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
        if (_blockerCanvasObject != null)
            _blockerCanvasObject.SetActive(true);
        if (_blockerObject != null)
            _blockerObject.SetActive(true);
        if (_inputSink != null)
            _inputSink.CaptureFocus();
        _isAttached = true;
    }

    public void Detach()
    {
        if (!_isAttached && _blockerCanvasObject == null && _blockerObject == null)
            return;

        Exception? failure = null;
        try
        {
            _inputSink?.ReleaseFocus();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        try
        {
            _blockerCanvasObject?.SetActive(false);
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }
        try
        {
            _blockerObject?.SetActive(false);
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }
        _isAttached = false;
        _owner = null;
        if (failure != null)
            throw failure;
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
        try
        {
            _inputSink?.ReleaseFocus();
        }
        catch
        {
            // Teardown is best-effort and must never strand input focus.
        }
        try
        {
            _blockerCanvasObject?.SetActive(false);
        }
        catch
        {
            // Destroy below remains the final cleanup path.
        }
        try
        {
            _blockerObject?.SetActive(false);
        }
        catch
        {
            // Destroy below remains the final cleanup path.
        }
        try
        {
            if (_blockerCanvasObject != null)
                UnityEngine.Object.Destroy(_blockerCanvasObject);
        }
        catch
        {
            // Unity teardown must not escape plugin disposal.
        }

        _blockerCanvasObject = null;
        _blockerObject = null;
        _inputSink = null;
        _isAttached = false;
        _owner = null;
    }
}
