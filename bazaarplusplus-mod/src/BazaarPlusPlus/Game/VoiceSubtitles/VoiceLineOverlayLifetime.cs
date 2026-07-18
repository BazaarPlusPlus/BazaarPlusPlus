#nullable enable

using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceLineOverlayLifetime : MonoBehaviour
{
    private GameObject? _labelObject;
    private Func<bool>? _isPlaybackStoppedOrStopping;
    private Func<string>? _playbackStateText;
    private float _hideAt;
    private float _shownAt;
    private int _displayId;
    private int _attemptId;
    private string _stem = "<none>";

    private void Awake()
    {
        enabled = false;
    }

    public void Initialize(GameObject labelObject)
    {
        _labelObject = labelObject;
        enabled = false;
    }

    public void ShowUntilVoiceStops(
        Func<bool>? isPlaybackStoppedOrStopping,
        Func<string>? playbackStateText,
        float fallbackDurationSeconds,
        int displayId,
        int attemptId,
        string stem
    )
    {
        _isPlaybackStoppedOrStopping = isPlaybackStoppedOrStopping;
        _playbackStateText = playbackStateText;
        _shownAt = Time.unscaledTime;
        _hideAt = _shownAt + Mathf.Max(0.2f, fallbackDurationSeconds);
        _displayId = displayId;
        _attemptId = attemptId;
        _stem = stem;
        enabled = true;
    }

    private void Update()
    {
        if (_labelObject == null || !_labelObject.activeSelf)
            return;

        if (_isPlaybackStoppedOrStopping != null)
        {
            if (IsPlaybackStoppedOrStopping())
            {
                HidePlaybackStopped();
                return;
            }
        }

        if (Time.unscaledTime >= _hideAt)
            HideFallbackTimeout();
    }

    private void HidePlaybackStopped()
    {
        Hide(VoiceSubtitlesLogReasonCode.PlaybackStopped);
    }

    private void HideFallbackTimeout()
    {
        Hide(VoiceSubtitlesLogReasonCode.FallbackTimeout);
    }

    private void Hide(VoiceSubtitlesLogReasonCode reasonCode)
    {
        if (_labelObject != null)
            _labelObject.SetActive(false);
        BppLog.DebugEvent(
            VoiceSubtitlesDisplayLogEvents.DisplayHidden,
            () =>
                [
                    VoiceSubtitlesDisplayLogEvents.DisplayHiddenDisplayId.Bind(_displayId),
                    VoiceSubtitlesDisplayLogEvents.DisplayHiddenAttemptId.Bind(_attemptId),
                    VoiceSubtitlesDisplayLogEvents.DisplayHiddenStem.Bind(_stem),
                    VoiceSubtitlesDisplayLogEvents.DisplayHiddenReasonCode.Bind(reasonCode),
                    VoiceSubtitlesDisplayLogEvents.DisplayHiddenElapsedMs.Bind(
                        (long)
                            Math.Round(
                                Mathf.Max(0f, Time.unscaledTime - _shownAt) * 1000f,
                                MidpointRounding.AwayFromZero
                            )
                    ),
                    VoiceSubtitlesDisplayLogEvents.DisplayHiddenPlaybackState.Bind(
                        PlaybackStateText()
                    ),
                ]
        );
        _isPlaybackStoppedOrStopping = null;
        _playbackStateText = null;
        enabled = false;
    }

    private bool IsPlaybackStoppedOrStopping()
    {
        try
        {
            return _isPlaybackStoppedOrStopping?.Invoke() == true;
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegraded,
                ex,
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedDisplayId.Bind(_displayId),
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedAttemptId.Bind(_attemptId),
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedReasonCode.Bind(
                    VoiceSubtitlesLogReasonCode.PlaybackQueryException
                )
            );
            return false;
        }
    }

    private string PlaybackStateText()
    {
        try
        {
            return _playbackStateText?.Invoke() ?? "<none>";
        }
        catch
        {
            return "<error>";
        }
    }
}
