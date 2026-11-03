using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

/// <summary>
/// Screen Fill (ADR-0025 decision 5), a Native Adjustment: the game fills a landscape screen
/// narrower than 16:10, such as an iPad's or a foldable's inner display, instead of letterboxing
/// it. Core's <see cref="ScreenFill"/> decides; this runs it; the host reads and writes the game
/// (<see cref="IScreenFillHost"/>). The game's letterbox clamps the aspect ratio it draws to 16:10
/// through 21:9 and crops everything outside it; each frame lowers that floor to 1:1 (the game's
/// static initializer may run after a first write) and, every <see cref="CanvasEvery"/> frames,
/// matches the game's canvases (<see cref="TouchScreenFillCanvases"/>), as canvases come with
/// scenes and popups. After each camera update the board is drawn nearer
/// (<see cref="ScreenFill.WidenedFieldOfView"/>), lowered where the HUD gathers above it, and the
/// day clock moved into that band (<see cref="TouchDayClock"/>); the day-change overlay follows
/// the changed camera (<see cref="TouchDayChangeOverlay"/>). The HUD ticks it
/// (<see cref="Hud.TouchHud"/>); each host's Cinemachine patch calls
/// <see cref="AfterCameraUpdate"/>. Main thread only.
/// </summary>
internal sealed class TouchScreenFill
{
    private const int CanvasEvery = 30;
    private const int LoggedFailures = 5;

    private static readonly Vector2Int Floor = new(ScreenFill.FloorWidth, ScreenFill.FloorHeight);

    private readonly IScreenFillHost _host;
    private readonly TouchScreenFillCanvases _canvases;
    private readonly TouchDayClock _clock;
    private readonly TouchDayChangeOverlay _dayChange;

    // The field of view and the position last written to each camera, by instance, with the game's
    // field of view each replaced and the offset each was raised by.
    private readonly Dictionary<int, float> _widened = new();
    private readonly Dictionary<int, float> _gameFieldOfView = new();
    private readonly Dictionary<int, Vector3> _raised = new();
    private readonly Dictionary<int, Vector3> _lift = new();

    private int _frame;
    private bool _logged;
    private int _tickFailures;
    private int _cameraFailures;

    public TouchScreenFill(IScreenFillHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _canvases = new TouchScreenFillCanvases(host);
        _clock = new TouchDayClock(host);
        _dayChange = new TouchDayChangeOverlay(host);
    }

    /// <summary>
    /// While the board is drawn nearer, its top edge in screen pixels down from the top: the HUD
    /// gathers in the band above it. Null otherwise.
    /// </summary>
    public float? BoardTop { get; private set; }

    /// <summary>Every frame, from the HUD's tick. Catches its own failure.</summary>
    public void Tick()
    {
        try
        {
            if (++_frame % CanvasEvery == 0)
                _canvases.Apply(ScreenFill.Aspect(Screen.width, Screen.height));
            if (!_host.LowerLetterBox(Floor, out var previous) || _logged)
                return;
            _logged = true;
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.screen_fill.applied"),
                ("previous_floor", $"{previous.x}:{previous.y}"),
                ("screen", $"{Screen.width}x{Screen.height}")
            );
        }
        catch (Exception exception)
        {
            if (++_tickFailures <= LoggedFailures)
                BppLog.WarnEvent(
                    new BppLogEvent(BppLogFeatureScope.Touch, "touch.screen_fill.tick_failed"),
                    exception
                );
        }
    }

    /// <summary>
    /// Called after Cinemachine's brain updated <paramref name="camera"/> this frame: widens its
    /// field of view on a screen narrower than 16:10, then, where the HUD gathers above the board,
    /// lowers the board to the screen's bottom edge and places the day clock in the band left
    /// above it, and keeps the day-change overlay over the whole screen. The brain writes the lens
    /// and the position again on most frames; a frame it skipped still holds the value written
    /// here, which is left alone so the change never compounds. Catches its own failure.
    /// </summary>
    public void AfterCameraUpdate(Camera? camera)
    {
        if (!MobilePlatform.Port.IsAlive(camera))
            return;
        try
        {
            Vector2? corner =
                Widen(camera!) && TryLowerBoard(camera!, out var topLeft) ? topLeft : null;
            BoardTop = corner?.y;
            _clock.Place(camera!, corner);
            _dayChange.Place(camera!, Lift(camera!), Stretch(camera!));
        }
        catch (Exception exception)
        {
            BoardTop = null;
            if (++_cameraFailures <= LoggedFailures)
                BppLog.WarnEvent(
                    new BppLogEvent(BppLogFeatureScope.Touch, "touch.screen_fill.camera_failed"),
                    exception
                );
        }
    }

    // Widens a perspective camera's field of view on a screen narrower than 16:10; true when the
    // HUD gathers above the board there (ScreenFill.GathersHudAbove).
    private bool Widen(Camera camera)
    {
        if (camera.orthographic || camera.usePhysicalProperties)
            return false;
        var aspect = camera.aspect;
        if (!ScreenFill.Fills(aspect))
            return false;
        var key = camera.GetInstanceID();
        var fieldOfView = camera.fieldOfView;
        var above = ScreenFill.GathersHudAbove(aspect);
        if (_widened.TryGetValue(key, out var written) && written == fieldOfView)
            return above;
        camera.fieldOfView = ScreenFill.WidenedFieldOfView(fieldOfView, aspect);
        // Read back: the camera clamps the value it stores.
        _widened[key] = camera.fieldOfView;
        _gameFieldOfView[key] = fieldOfView;
        return above;
    }

    // How many times smaller the camera draws the world than the game's lens did; 1 unless it
    // still holds the field of view written here.
    private float Stretch(Camera camera)
    {
        var key = camera.GetInstanceID();
        return
            _widened.TryGetValue(key, out var written)
            && written == camera.fieldOfView
            && _gameFieldOfView.TryGetValue(key, out var game)
            ? ScreenFill.ViewStretch(game, written)
            : 1f;
    }

    // The world offset the camera was raised by; zero unless it still holds the position written
    // here.
    private Vector3 Lift(Camera camera)
    {
        var key = camera.GetInstanceID();
        return
            _raised.TryGetValue(key, out var written)
            && written == camera.transform.position
            && _lift.TryGetValue(key, out var lift)
            ? lift
            : Vector3.zero;
    }

    // Raises the camera by ScreenFill.BoardDrop, so the room left above and below the nearer board
    // is one band above it, and gives the board's top-left corner in screen pixels down from the
    // top (ScreenFill.BoardTopLeft: the vault's bottom-right mirrored through the board's centre).
    private bool TryLowerBoard(Camera camera, out Vector2 topLeft)
    {
        topLeft = default;
        var port = MobilePlatform.Port;
        var center = _host.BoardCenter();
        if (!port.IsAlive(center) || !port.TryReadVault(Screen.height, out var vault))
            return false;
        var transform = camera.transform;
        var key = camera.GetInstanceID();
        if (!_raised.TryGetValue(key, out var written) || written != transform.position)
        {
            var depth = camera.WorldToScreenPoint(center!.position).z;
            if (depth <= 0f)
                return false;
            var drop = ScreenFill.BoardDrop(Screen.height, Screen.safeArea.yMin, vault.Bottom);
            var lift = Vector3.zero;
            if (drop > 0f)
            {
                lift =
                    transform.up
                    * (
                        drop
                        * ScreenFill.WorldUnitsPerPixel(depth, camera.fieldOfView, Screen.height)
                    );
                transform.position += lift;
            }
            _raised[key] = transform.position;
            _lift[key] = lift;
            if (drop > 0f && !port.TryReadVault(Screen.height, out vault))
                return false;
        }
        var middle = camera.WorldToScreenPoint(center!.position);
        if (middle.z <= 0f)
            return false;
        var (x, y) = ScreenFill.BoardTopLeft(
            middle.x,
            Screen.height - middle.y,
            vault.Right,
            vault.Bottom
        );
        topLeft = new Vector2(x, y);
        return true;
    }
}
