#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Screenshots;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed partial class CombatStatusBar : MonoBehaviour
{
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

        _visualBlend = AdvanceVisualBlend(
            _visualBlend,
            IsCombatPlaybackActive,
            Time.unscaledDeltaTime
        );

        EnsureUi();
        RefreshUi();
    }

    private bool ShouldDraw()
    {
        EnsureConfigStateInitialized();
        return _screenshotSuppressionCount == 0 && ShouldRenderForState(IsEnabled());
    }

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
        BeginCombatPlayback();
    }

    private static void OnCombatEnded()
    {
        EndCombatPlayback();
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
