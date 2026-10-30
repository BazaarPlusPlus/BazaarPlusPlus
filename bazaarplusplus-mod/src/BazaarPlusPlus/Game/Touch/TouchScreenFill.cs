#nullable enable
using BazaarPlusPlus.GameInterop.ScreenFill;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using Fill = BazaarPlusPlus.Core.Ui.ScreenFill;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// Screen Fill on a mobile platform (ADR-0024, ADR-0025 decision 5), as the Android host's
/// <c>AndroidScreenFill</c>: the game fills a landscape screen narrower than 16:10, such as an
/// iPad's, instead of letterboxing it. Core's <see cref="Fill"/> decides; the
/// <c>GameInterop/ScreenFill</c> adapters read and write the game. Each frame lowers the letterbox
/// floor (the class's static initializer may run after a first write) and, every
/// <see cref="CanvasEvery"/> frames, matches the game's canvases; after each camera update
/// (<c>Patches/ScreenFill/TouchScreenFillCameraPatch</c>) the board is drawn nearer, lowered where
/// the HUD gathers above it, and the day clock moved into that band. Mounted only on a mobile
/// platform; main thread only.
/// </summary>
internal sealed class TouchScreenFill : MonoBehaviour
{
    private const int CanvasEvery = 30;

    private static readonly Vector2Int Floor = new(Fill.FloorWidth, Fill.FloorHeight);
    private static readonly ScreenFillCamera Camera = new();
    private static readonly ScreenFillDayClock Clock = new();
    private static bool _mounted;
    private static bool _logged;
    private static int _cameraFailures;

    private int _frame;

    /// <summary>
    /// While the board is drawn nearer, its top edge in screen pixels down from the top: the HUD
    /// gathers in the band above it. Null otherwise.
    /// </summary>
    public static float? BoardTop { get; private set; }

    private void OnEnable() => _mounted = true;

    private void OnDisable()
    {
        _mounted = false;
        BoardTop = null;
    }

    private void Update()
    {
        try
        {
            if (++_frame % CanvasEvery == 0)
                ScreenFillCanvases.Apply(Fill.Aspect(Screen.width, Screen.height));
            if (!ScreenFillLetterBox.Lower(Floor, out var previous) || _logged)
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
            BppLog.WarnEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.screen_fill.tick_failed"),
                exception
            );
        }
    }

    /// <summary>
    /// Called after Cinemachine's brain updated <paramref name="camera"/> this frame: widens its
    /// field of view on a screen narrower than 16:10, then, where the HUD gathers above the board,
    /// lowers the board to the screen's bottom edge and places the day clock in the band above it.
    /// </summary>
    public static void AfterCameraUpdate(UnityEngine.Camera? camera)
    {
        if (!_mounted || camera == null)
            return;
        try
        {
            Vector2? corner =
                Camera.Widen(camera) && Camera.TryLowerBoard(camera, out var topLeft)
                    ? topLeft
                    : null;
            BoardTop = corner?.y;
            Clock.Place(camera, corner);
        }
        catch (Exception exception)
        {
            BoardTop = null;
            if (++_cameraFailures <= 5)
                BppLog.WarnEvent(
                    new BppLogEvent(BppLogFeatureScope.Touch, "touch.screen_fill.camera_failed"),
                    exception
                );
        }
    }
}
