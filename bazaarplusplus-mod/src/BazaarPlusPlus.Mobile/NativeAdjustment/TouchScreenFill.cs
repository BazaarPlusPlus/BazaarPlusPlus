using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.NativeAdjustment;

// Reapply the letterbox floor each frame because the game's static initializer can overwrite it.
internal sealed class TouchScreenFill
{
    private const int CanvasEvery = 30;
    private const int LoggedFailures = 5;

    private static readonly Vector2Int Floor = new(ScreenFill.FloorWidth, ScreenFill.FloorHeight);

    private readonly IScreenFillHost _host;
    private readonly TouchScreenFillCanvases _canvases;
    private readonly TouchDayClock _clock;
    private readonly TouchDayChangeOverlay _dayChange;

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

    // Screen pixels measured down from the top, for the HUD band above the board.
    public float? BoardTop { get; private set; }

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

    // Cinemachine may skip a frame and leave our last lens and position in place.
    // Reuse those values so the adjustment cannot compound.
    public void AfterCameraUpdate(Camera? camera)
    {
        if (!Il2CppUi.IsAlive(camera))
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

    // Recover the board's top-left by mirroring the vault's bottom-right through its centre.
    private bool TryLowerBoard(Camera camera, out Vector2 topLeft)
    {
        topLeft = default;
        var port = MobilePlatform.Port;
        var center = _host.BoardCenter();
        if (!Il2CppUi.IsAlive(center) || !port.TryReadVault(Screen.height, out var vault))
            return false;
        var transform = camera.transform;
        var key = camera.GetInstanceID();
        if (!_raised.TryGetValue(key, out var written) || written != transform.position)
        {
            var depth = camera.WorldToScreenPoint(center.position).z;
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
        var middle = camera.WorldToScreenPoint(center.position);
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
