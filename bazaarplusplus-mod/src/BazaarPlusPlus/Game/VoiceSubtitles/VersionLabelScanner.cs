#nullable enable

using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VersionLabelScanner : MonoBehaviour
{
    private const float ScanIntervalSeconds = 0.75f;
    private const float BackoffScanIntervalSeconds = 5f;
    private const int FullScanMissesBeforeBackoff = 3;
    private float _nextScanAt;
    private float _nextFullScanAt;
    private int _consecutiveFullScanMisses;
    private string? _cachedVersionLabelPath;

    private void Update()
    {
        if (Time.unscaledTime < _nextScanAt)
            return;

        _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
        if (!VoiceSubtitlesGate.IsEnabled())
        {
            ResetBackoff();
            return;
        }

        if (VoiceLineDisplay.IsMountedFromVersionLabel)
            return;

        var versionLabel = FindCachedVersionLabel();
        var usedFullScan = false;
        if (versionLabel == null && Time.unscaledTime >= _nextFullScanAt)
        {
            usedFullScan = true;
            versionLabel = FindVisibleVersionLabel();
        }

        if (versionLabel != null)
        {
            var anchorHasChineseCoverage = FontDiagnostics.HasChineseCoverage(versionLabel.font);
            var typographyWaiting =
                !anchorHasChineseCoverage
                && NativeGameTypography.PrepareOwnedText(out _)
                    == NativeGameTypography.Outcome.Waiting;
            if (ShouldDelayMount(anchorHasChineseCoverage, typographyWaiting))
                return;

            VoiceLineDisplay.MountFromVersionLabel(versionLabel);
            if (VoiceLineDisplay.IsMountedFromVersionLabel)
            {
                _cachedVersionLabelPath = BuildPath(versionLabel.transform);
                ResetBackoff();
            }
            return;
        }

        if (usedFullScan)
            RecordFullScanMiss();
    }

    internal static bool ShouldDelayMount(
        bool anchorHasChineseCoverage,
        bool nativeTypographyWaiting
    ) => !anchorHasChineseCoverage && nativeTypographyWaiting;

    private TextMeshProUGUI? FindCachedVersionLabel()
    {
        if (string.IsNullOrWhiteSpace(_cachedVersionLabelPath))
            return null;

        var cachedObject = GameObject.Find(_cachedVersionLabelPath!);
        if (cachedObject == null)
            return null;

        var label = cachedObject.GetComponent<TextMeshProUGUI>();
        return IsUsableVersionLabel(label) ? label : null;
    }

    private void RecordFullScanMiss()
    {
        _consecutiveFullScanMisses++;
        _nextFullScanAt =
            Time.unscaledTime
            + (
                _consecutiveFullScanMisses >= FullScanMissesBeforeBackoff
                    ? BackoffScanIntervalSeconds
                    : ScanIntervalSeconds
            );
    }

    private void ResetBackoff()
    {
        _consecutiveFullScanMisses = 0;
        _nextFullScanAt = 0f;
        _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
    }

    private static TextMeshProUGUI? FindVisibleVersionLabel()
    {
        TextMeshProUGUI? best = null;
        var bestScore = float.NegativeInfinity;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if (!IsUsableVersionLabel(candidate))
                continue;

            var rect = candidate.rectTransform;
            var score = candidate.transform.root.gameObject.scene.isLoaded ? 1000f : 0f;
            score += candidate.gameObject.activeInHierarchy ? 100f : 0f;
            score += -rect.position.x * 0.01f;
            score += -rect.position.y * 0.01f;

            if (score <= bestScore)
                continue;

            best = candidate;
            bestScore = score;
        }

        if (best != null)
            BppLog.DebugEvent(
                VoiceSubtitlesDisplayLogEvents.MountAnchorSelected,
                () =>
                    [
                        VoiceSubtitlesDisplayLogEvents.MountAnchorPath.Bind(
                            BuildPath(best.transform)
                        ),
                        VoiceSubtitlesDisplayLogEvents.MountAnchorLabelText.Bind(best.text),
                    ]
            );

        return best;
    }

    private static bool IsUsableVersionLabel(TextMeshProUGUI? text)
    {
        if (text == null)
            return false;
        if (text.name.StartsWith("BazaarLine_", StringComparison.Ordinal))
            return false;
        if (text.transform.root.name.StartsWith("BazaarLine_", StringComparison.Ordinal))
            return false;
        if (!text.gameObject.activeInHierarchy)
            return false;
        if (!text.transform.root.gameObject.scene.isLoaded)
            return false;
        if (string.IsNullOrWhiteSpace(text.text))
            return false;

        return text.text.TrimStart().StartsWith("Version:", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPath(Transform transform)
    {
        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
