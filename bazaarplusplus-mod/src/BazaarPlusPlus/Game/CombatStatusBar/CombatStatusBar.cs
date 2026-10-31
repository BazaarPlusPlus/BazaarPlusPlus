#nullable enable
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar : MonoBehaviour
{
    /// <summary>The playback state the bar, the speed patch, and the frame module share.</summary>
    internal static CombatStatusBarState State { get; } = new(new GameServiceManagerPauseControl());

    private static CombatStatusBar? _current;
    private static IBppServices? _services;
    private float _visualBlend;
    private int _screenshotSuppressionCount;

    private void Awake()
    {
        _current = this;
    }

    private void OnEnable()
    {
        Events.CombatStarted.AddListener(OnCombatStarted, this);
        Events.CombatEnded.AddListener(OnCombatEnded, this);
        EnsureConfigStateInitialized();
        EnsureUi();
        RefreshUi();
    }

    private void OnDisable()
    {
        Events.CombatStarted.RemoveListener(OnCombatStarted);
        Events.CombatEnded.RemoveListener(OnCombatEnded);
        SetUiVisible(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;
        DisposeUi();
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    private void Update()
    {
        EnsureConfigStateInitialized();

        _visualBlend = CombatStatusBarState.AdvanceVisualBlend(
            _visualBlend,
            State.IsCombatPlaybackActive,
            Time.unscaledDeltaTime
        );
    }

    private void LateUpdate()
    {
        EnsureUi();
        RefreshUi();
    }

    private static void EnsureConfigStateInitialized()
    {
        if (_services != null)
            State.BindSpeedSetting(_services.Config.CombatStatusBarSpeedMultiplierConfig);
    }

    private bool ShouldDraw()
    {
        EnsureConfigStateInitialized();
        return _screenshotSuppressionCount == 0
            && _services != null
            && CombatStatusBarState.ShouldRenderForState(
                _services.Config.EnableCombatStatusBarConfig.Value,
                _services.RunContext,
                _services.GameStateProbe
            );
    }

    /// <summary>
    /// The Mobile UI's combat clock's rule beyond its setting (TouchHudMount): a run or a replay
    /// is on, and no end-of-run screenshot is being taken.
    /// </summary>
    internal static bool ShowsTouchClock() =>
        _current is { _screenshotSuppressionCount: 0 }
        && _services != null
        && CombatStatusBarState.ShouldRenderForState(
            true,
            _services.RunContext,
            _services.GameStateProbe
        );

    internal static IDisposable? BeginScreenshotSuppression()
    {
        return _current?.BeginInstanceScreenshotSuppression();
    }

    private IDisposable BeginInstanceScreenshotSuppression()
    {
        _screenshotSuppressionCount++;
        SetUiVisible(false);
        return new ScreenshotSuppressionLease(this);
    }

    private void EndInstanceScreenshotSuppression()
    {
        if (_screenshotSuppressionCount > 0)
            _screenshotSuppressionCount--;

        RefreshUi();
    }

    private static void OnCombatStarted()
    {
        State.BeginCombatPlayback();
    }

    private static void OnCombatEnded()
    {
        State.EndCombatPlayback();
    }

    private sealed class GameServiceManagerPauseControl : ICombatPauseControl
    {
        public bool IsAvailable => Singleton<GameServiceManager>.Instance != null;

        public bool TrySetPaused(bool paused, out bool isPaused)
        {
            var gameServiceManager = Singleton<GameServiceManager>.Instance;
            if (gameServiceManager == null)
            {
                isPaused = false;
                return false;
            }

            gameServiceManager.PauseOrUnpauseGame(paused);
            isPaused = gameServiceManager.GamePaused;
            return true;
        }
    }

    private sealed class ScreenshotSuppressionLease(CombatStatusBar owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.EndInstanceScreenshotSuppression();
        }
    }
}
