#nullable enable

using System.Collections.Concurrent;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.VoiceSubtitles.Settings;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceLineDisplay
{
    private const float ScaleComparisonTolerance = 0.0001f;
    private const float BilingualChineseScaleMultiplier = 1.08f;
    internal const int MaxQueuedShows = 8;

    private static GameObject? _labelRoot;
    private static TextMeshProUGUI? _combinedLabel;
    private static TextMeshProUGUI? _englishLabel;
    private static TextMeshProUGUI? _chineseUiLabel;
    private static VoiceLineOverlayLifetime? _lifetime;
    private static bool _mountedFromVersionLabel;
    private static int _nextDisplayId;
    private static float _secondLineOffset = -28f;
    private static RectTransform? _sourceRect;
    private static TextMeshProUGUI? _sourceLabel;
    private static readonly ConcurrentQueue<VoiceSubtitleCue> QueuedShows = new();
    private static readonly VoiceSubtitlesSettingsLogState SettingsLogState = new();

    public static bool IsMountedFromVersionLabel =>
        _mountedFromVersionLabel && CurrentLabelObject != null;

    public static void MountFromVersionLabel(TextMeshProUGUI versionLabel)
    {
        if (versionLabel == null)
            return;

        if (IsMountedFromVersionLabel)
            return;

        var stage = "destroy-existing";

        try
        {
            DestroyCurrentLabel();

            stage = "create-game-object";
            var labelObject = new GameObject("BazaarLine_SubtitleLabel", typeof(RectTransform));
            labelObject.SetActive(false);

            stage = "set-parent";
            labelObject.transform.SetParent(
                versionLabel.transform.parent,
                worldPositionStays: false
            );

            stage = "add-canvas-renderer";
            labelObject.AddComponent<CanvasRenderer>();

            stage = "add-layout-element";
            var layoutElement = labelObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            stage = "store-source-label";
            _sourceRect = versionLabel.rectTransform;
            _sourceLabel = versionLabel;
            FontDiagnostics.LogOnce(versionLabel);

            stage = "add-subtitle-renderer";
            _labelRoot = labelObject;
            if (FontDiagnostics.HasChineseCoverage(versionLabel.font))
                _combinedLabel = labelObject.AddComponent<TextMeshProUGUI>();
            else
            {
                _englishLabel = CreateEnglishLabel(labelObject.transform);
                _chineseUiLabel = CreateChineseUiLabel(labelObject.transform);
            }

            if (_combinedLabel == null && (_englishLabel == null || _chineseUiLabel == null))
            {
                UnityEngine.Object.Destroy(labelObject);
                return;
            }

            stage = "add-lifetime";
            _lifetime = labelObject.AddComponent<VoiceLineOverlayLifetime>();
            _lifetime.Initialize(labelObject);

            _mountedFromVersionLabel = true;

            stage = "apply-settings";
            TryApplySettingsToLabel(VoiceSubtitlesSettingsPhase.Mount);
            BppLog.DebugEvent(
                VoiceSubtitlesDisplayLogEvents.OverlayMounted,
                () =>
                    [
                        VoiceSubtitlesDisplayLogEvents.OverlayMountedRenderer.Bind(
                            RendererDescription()
                        ),
                        VoiceSubtitlesDisplayLogEvents.OverlayMountedAnchorPath.Bind(
                            BuildPath(versionLabel.transform)
                        ),
                    ]
            );
        }
        catch (Exception ex)
        {
            DestroyCurrentLabel();
            BppLog.ErrorEvent(
                VoiceSubtitlesDisplayLogEvents.OverlayFailed,
                ex,
                VoiceSubtitlesDisplayLogEvents.OverlayFailedStage.Bind(stage),
                VoiceSubtitlesDisplayLogEvents.OverlayFailedAnchorPath.Bind(
                    SafeAnchorPath(versionLabel)
                ),
                VoiceSubtitlesDisplayLogEvents.OverlayFailedAnchorText.Bind(
                    SafeAnchorText(versionLabel)
                ),
                VoiceSubtitlesDisplayLogEvents.OverlayFailedReasonCode.Bind(
                    VoiceSubtitlesLogReasonCode.MountException
                )
            );
        }
    }

    public static void Show(VoiceSubtitleCue cue)
    {
        if (!VoiceSubtitlesGate.IsEnabled())
            return;

        var displayId = ++_nextDisplayId;
        var line = cue.Line;
        var text = BuildDisplayText(line);
        if (text.IsEmpty)
        {
            BppLog.DebugEvent(
                VoiceSubtitlesDisplayLogEvents.DisplaySkipped,
                () =>
                    [
                        VoiceSubtitlesDisplayLogEvents.DisplaySkippedDisplayId.Bind(displayId),
                        VoiceSubtitlesDisplayLogEvents.DisplaySkippedAttemptId.Bind(cue.AttemptId),
                        VoiceSubtitlesDisplayLogEvents.DisplaySkippedStem.Bind(line.Stem),
                        VoiceSubtitlesDisplayLogEvents.DisplaySkippedReasonCode.Bind(
                            VoiceSubtitlesLogReasonCode.EmptyText
                        ),
                    ]
            );
            return;
        }

        var fallbackDuration =
            cue.EventDurationSeconds > 0f
                ? cue.EventDurationSeconds + 0.15f
                : Math.Max(1f, line.DurationSeconds + 0.15f);

        ShowRaw(text, cue, fallbackDuration, displayId, line.Stem);
    }

    public static void QueueShow(VoiceSubtitleCue cue)
    {
        if (!VoiceSubtitlesGate.IsEnabled())
            return;

        EnqueueQueuedShow(cue);
    }

    internal static void EnqueueQueuedShow(VoiceSubtitleCue cue)
    {
        QueuedShows.Enqueue(cue);
        while (QueuedShows.Count > MaxQueuedShows)
            if (!QueuedShows.TryDequeue(out _))
                break;
    }

    public static void ProcessQueuedShows()
    {
        if (!VoiceSubtitlesGate.IsEnabled())
        {
            while (QueuedShows.TryDequeue(out _)) { }
            return;
        }

        ProcessQueuedShows(IsMountedFromVersionLabel, Show);
    }

    internal static void ProcessQueuedShows(bool isMounted, Action<VoiceSubtitleCue> showQueuedCue)
    {
        if (showQueuedCue == null)
            throw new ArgumentNullException(nameof(showQueuedCue));

        if (!isMounted)
        {
            while (
                QueuedShows.TryPeek(out var queued)
                && ShouldDiscardQueuedCue(queued)
                && QueuedShows.TryDequeue(out _)
            ) { }
            return;
        }

        while (QueuedShows.TryDequeue(out var queued))
            if (!ShouldDiscardQueuedCue(queued))
                showQueuedCue(queued);
    }

    private static bool ShouldDiscardQueuedCue(VoiceSubtitleCue cue)
    {
        try
        {
            return cue.IsPlaybackStoppedOrStopping?.Invoke() == true;
        }
        catch (Exception ex)
        {
            BppLog.WarnEvent(
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegraded,
                ex,
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedDisplayId.Bind(null),
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedAttemptId.Bind(
                    cue.AttemptId
                ),
                VoiceSubtitlesDisplayLogEvents.PlaybackTrackingDegradedReasonCode.Bind(
                    VoiceSubtitlesLogReasonCode.PlaybackQueryException
                )
            );
            return true;
        }
    }

    public static void Reset()
    {
        DestroyCurrentLabel();
        _nextDisplayId = 0;
        while (QueuedShows.TryDequeue(out _)) { }
    }

    private static void DestroyCurrentLabel()
    {
        if (_labelRoot != null)
            UnityEngine.Object.Destroy(_labelRoot);

        _labelRoot = null;
        _combinedLabel = null;
        _englishLabel = null;
        _chineseUiLabel = null;
        _lifetime = null;
        _mountedFromVersionLabel = false;
        _sourceRect = null;
        _sourceLabel = null;
    }

    private static void ShowRaw(
        DisplayText text,
        VoiceSubtitleCue cue,
        float durationSeconds,
        int displayId,
        string stem
    )
    {
        var labelObject = CurrentLabelObject;
        if (labelObject == null || _lifetime == null)
        {
            BppLog.ErrorEvent(
                VoiceSubtitleDisplayLogEvents.DisplayFailed,
                VoiceSubtitleDisplayLogEvents.DisplayId.Bind(displayId),
                VoiceSubtitleDisplayLogEvents.AttemptId.Bind(cue.AttemptId),
                VoiceSubtitleDisplayLogEvents.Stem.Bind(stem),
                VoiceSubtitleDisplayLogEvents.ReasonCode.Bind(
                    VoiceSubtitleDisplayLogReasonCode.LabelUnavailable
                )
            );
            return;
        }

        var activeBefore = labelObject.activeSelf;
        TryApplySettingsToLabel(VoiceSubtitlesSettingsPhase.Show, text);
        ApplyText(text);
        labelObject.SetActive(true);
        _lifetime.ShowUntilVoiceStops(
            cue.IsPlaybackStoppedOrStopping,
            cue.PlaybackStateText,
            durationSeconds,
            displayId,
            cue.AttemptId,
            stem
        );
        BppLog.DebugEvent(
            VoiceSubtitlesDisplayLogEvents.DisplayRendered,
            () =>
                [
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedDisplayId.Bind(displayId),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedAttemptId.Bind(cue.AttemptId),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedStem.Bind(stem),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedEventDurationMs.Bind(
                        ToMilliseconds(cue.EventDurationSeconds)
                    ),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedLineDurationMs.Bind(
                        ToMilliseconds(cue.Line.DurationSeconds)
                    ),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedDisplayDurationMs.Bind(
                        ToMilliseconds(durationSeconds)
                    ),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedRenderer.Bind(
                        RendererDescription()
                    ),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedActiveBefore.Bind(activeBefore),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedPlaybackState.Bind(
                        SafePlaybackStateText(cue.PlaybackStateText)
                    ),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedEnglishText.Bind(text.English),
                    VoiceSubtitlesDisplayLogEvents.DisplayRenderedChineseText.Bind(text.Chinese),
                ]
        );
    }

    private static DisplayText BuildDisplayText(VoiceLine line)
    {
        var settings = VoiceLineSettings.Current;
        var english =
            settings.LanguageMode == SubtitleLanguageMode.ChineseOnly || ShouldHideEnglish(line)
                ? string.Empty
                : line.English.Trim();
        var chinese =
            settings.LanguageMode == SubtitleLanguageMode.EnglishOnly
                ? string.Empty
                : line.Chinese.Trim();
        chinese = ConvertCenteredChineseTrailingPunctuation(chinese, settings.Position);

        if (string.IsNullOrEmpty(english) && string.IsNullOrEmpty(chinese))
            return DisplayText.Empty;

        return new DisplayText(english, chinese);
    }

    private static bool ShouldHideEnglish(VoiceLine line)
    {
        return line.Stem.IndexOf("Dooley", StringComparison.OrdinalIgnoreCase) >= 0
            && string.Equals(
                line.English.Trim(),
                line.Stem.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool TryApplySettingsToLabel(
        VoiceSubtitlesSettingsPhase phase,
        DisplayText? currentText = null
    )
    {
        if (CurrentLabelObject == null || _sourceRect == null || _sourceLabel == null)
            return false;

        try
        {
            var settings = VoiceLineSettings.Current;
            ConfigureRect(_sourceRect, CurrentRectTransform!, settings.Position);
            if (_combinedLabel != null)
                ConfigureCombinedText(_sourceLabel, _combinedLabel, settings);
            else
                ConfigureSplitText(
                    _sourceLabel,
                    _englishLabel,
                    _chineseUiLabel,
                    settings,
                    currentText
                );
            SettingsLogState.ReportSucceeded(phase);
            return true;
        }
        catch (Exception ex)
        {
            SettingsLogState.ReportDegraded(phase, ex);
            return false;
        }
    }

    private static void ConfigureRect(
        RectTransform source,
        RectTransform target,
        SubtitlePosition position
    )
    {
        var anchorX = position switch
        {
            SubtitlePosition.TopRight => 1f,
            SubtitlePosition.TopCenter => 0.5f,
            _ => 0f,
        };
        target.anchorMin = new Vector2(anchorX, 1f);
        target.anchorMax = new Vector2(anchorX, 1f);
        target.pivot = new Vector2(anchorX, 1f);
        target.localScale = source.localScale;
        target.localRotation = Quaternion.identity;

        var xMargin = source.anchoredPosition.x;
        var yMargin = Math.Abs(source.anchoredPosition.y);
        var xPosition = position switch
        {
            SubtitlePosition.TopRight => -Math.Abs(xMargin),
            SubtitlePosition.TopCenter => 0f,
            _ => xMargin,
        };

        target.anchoredPosition = new Vector2(xPosition, -yMargin);
        target.sizeDelta = new Vector2(
            Math.Max(source.sizeDelta.x, 760f),
            Math.Max(source.sizeDelta.y * 2.8f, 64f)
        );
    }

    private static void ConfigureCombinedText(
        TextMeshProUGUI source,
        TextMeshProUGUI target,
        VoiceLineSettings settings
    )
    {
        if (source.font != null)
            target.font = source.font;
        if (source.fontSharedMaterial != null)
            target.fontSharedMaterial = source.fontSharedMaterial;

        target.fontStyle = source.fontStyle;
        target.characterSpacing = source.characterSpacing;
        target.wordSpacing = source.wordSpacing;
        target.paragraphSpacing = source.paragraphSpacing;
        target.enableAutoSizing = source.enableAutoSizing;
        target.fontSizeMin = source.fontSizeMin;
        target.fontSizeMax = source.fontSizeMax;
        target.text = string.Empty;
        target.alignment = settings.Position switch
        {
            SubtitlePosition.TopRight => TextAlignmentOptions.TopRight,
            SubtitlePosition.TopCenter => TextAlignmentOptions.Top,
            _ => TextAlignmentOptions.TopLeft,
        };
        // Wrap long lines within the box width and never clip vertically: with a
        // fixed box height but font scale up to 2.5x, NoWrap + Ellipsis makes TMP's
        // GenerateTextMesh hit the m_characterCount = 0 branch (no ellipsis-insertion
        // candidate on an overflowing line) and drop the entire block. Overflow is not
        // in that truncation switch, so the text always renders.
        target.textWrappingMode = TextWrappingModes.Normal;
        target.overflowMode = TextOverflowModes.Overflow;
        target.richText = true;
        target.raycastTarget = false;
        target.fontSize = Math.Max(source.fontSize, 16f);
        target.lineSpacing = 8f;
        target.color = new Color(1f, 0.96f, 0.84f, 0.96f);
    }

    private static void ConfigureSplitText(
        TextMeshProUGUI source,
        TextMeshProUGUI? english,
        TextMeshProUGUI? chineseUi,
        VoiceLineSettings settings,
        DisplayText? currentText = null
    )
    {
        var englishFontSize = Math.Max(source.fontSize * settings.EnglishFontScale, 16f);
        var chineseFontScale = ResolveEffectiveChineseFontScale(settings, currentText);
        var chineseFontSize = Math.Max(source.fontSize * chineseFontScale, 16f);
        var lineHeight = Math.Max(englishFontSize, chineseFontSize) * 1.25f;

        if (english != null)
        {
            if (source.font != null)
                english.font = source.font;
            if (source.fontSharedMaterial != null)
                english.fontSharedMaterial = source.fontSharedMaterial;

            english.fontStyle = source.fontStyle;
            english.characterSpacing = source.characterSpacing;
            english.wordSpacing = source.wordSpacing;
            english.paragraphSpacing = source.paragraphSpacing;
            english.enableAutoSizing = false;
            english.text = string.Empty;
            english.alignment = settings.Position switch
            {
                SubtitlePosition.TopRight => TextAlignmentOptions.TopRight,
                SubtitlePosition.TopCenter => TextAlignmentOptions.Top,
                _ => TextAlignmentOptions.TopLeft,
            };
            english.textWrappingMode = TextWrappingModes.Normal;
            english.overflowMode = TextOverflowModes.Overflow;
            english.richText = false;
            english.raycastTarget = false;
            english.fontSize = englishFontSize;
            english.lineSpacing = 0f;
            english.color = new Color(1f, 0.96f, 0.84f, 0.96f);
        }

        if (chineseUi != null)
        {
            chineseUi.fontStyle = FontStyles.Normal;
            chineseUi.alignment = settings.Position switch
            {
                SubtitlePosition.TopRight => TextAlignmentOptions.TopRight,
                SubtitlePosition.TopCenter => TextAlignmentOptions.Top,
                _ => TextAlignmentOptions.TopLeft,
            };
            chineseUi.textWrappingMode = TextWrappingModes.Normal;
            chineseUi.overflowMode = TextOverflowModes.Overflow;
            chineseUi.richText = false;
            chineseUi.raycastTarget = false;
            chineseUi.fontSize = Math.Max(chineseFontSize, 16f);
            chineseUi.lineSpacing = 1f;
            chineseUi.color = new Color(1f, 0.96f, 0.84f, 0.96f);
            chineseUi.text = string.Empty;
        }

        _secondLineOffset = -lineHeight;
        ConfigureLineRect(english?.rectTransform, 0f, lineHeight);
        ConfigureLineRect(chineseUi?.rectTransform, _secondLineOffset, lineHeight);
    }

    private static TextMeshProUGUI? CreateEnglishLabel(Transform parent)
    {
        var labelObject = CreateChildLabelObject(parent, "BazaarLine_EnglishSubtitle");
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        return label;
    }

    private static TextMeshProUGUI? CreateChineseUiLabel(Transform parent)
    {
        if (
            NativeGameTypography.PrepareOwnedText(out var typography)
                != NativeGameTypography.Outcome.Ready
            || typography == null
        )
            return null;

        var labelObject = CreateChildLabelObject(parent, "BazaarLine_ChineseSubtitle");
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        if (typography.Apply(label) != NativeGameTypography.Outcome.Applied)
        {
            UnityEngine.Object.Destroy(labelObject);
            return null;
        }
        label.fontStyle = FontStyles.Normal;
        label.richText = false;
        label.raycastTarget = false;
        return label;
    }

    private static GameObject CreateChildLabelObject(Transform parent, string name)
    {
        var labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.transform.SetParent(parent, worldPositionStays: false);
        labelObject.AddComponent<CanvasRenderer>();
        return labelObject;
    }

    private static void ConfigureLineRect(RectTransform? rect, float yOffset, float lineHeight)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(0f, Math.Max(48f, lineHeight));
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static GameObject? CurrentLabelObject => _labelRoot;

    private static RectTransform? CurrentRectTransform =>
        _labelRoot != null ? _labelRoot.GetComponent<RectTransform>() : null;

    private static void ApplyText(DisplayText text)
    {
        if (_combinedLabel != null)
        {
            _combinedLabel.text = BuildCombinedText(text);
            _combinedLabel.gameObject.SetActive(!text.IsEmpty);
            return;
        }

        var hasEnglish = !string.IsNullOrEmpty(text.English);
        if (_englishLabel != null)
        {
            _englishLabel.text = text.English;
            _englishLabel.gameObject.SetActive(hasEnglish);
        }

        if (_chineseUiLabel != null)
        {
            _chineseUiLabel.text = text.Chinese;
            _chineseUiLabel.gameObject.SetActive(!string.IsNullOrEmpty(text.Chinese));
            // English now wraps to a variable number of lines, so push the Chinese row
            // below the English row's measured height instead of a fixed single-line
            // offset (which would overlap the later English lines at larger scales).
            var chineseOffset = hasEnglish ? -MeasureEnglishHeight(text.English) : 0f;
            ConfigureLineRect(
                _chineseUiLabel.rectTransform,
                chineseOffset,
                Math.Abs(_secondLineOffset)
            );
        }
    }

    private static float MeasureEnglishHeight(string englishText)
    {
        var minimum = Math.Abs(_secondLineOffset);
        if (_englishLabel == null || string.IsNullOrEmpty(englishText))
            return minimum;

        // Measure wrapped English height at the label's current width so the Chinese
        // row clears every English line. GetPreferredValues honours the label's
        // font/scale/wrapping without needing an active layout pass.
        var width = CurrentRectTransform != null ? CurrentRectTransform.rect.width : 760f;
        if (width < 1f)
            width = 760f;

        var measured = _englishLabel.GetPreferredValues(englishText, width, 0f).y;
        return Math.Max(measured, minimum);
    }

    private static string RendererDescription()
    {
        if (_combinedLabel != null)
            return $"combined=TextMeshProUGUI font={FontDiagnostics.DescribeFont(_combinedLabel.font)}";

        return "english=TextMeshProUGUI "
            + $"font={FontDiagnostics.DescribeFont(_englishLabel?.font)} "
            + $"chinese={ChineseRendererDescription()}";
    }

    private static string ChineseRendererDescription()
    {
        if (_chineseUiLabel != null)
        {
            return "TextMeshProUGUI "
                + $"font='{_chineseUiLabel.font?.name ?? "<null>"}' "
                + $"style={_chineseUiLabel.fontStyle}";
        }

        return "<none>";
    }

    private static string BuildCombinedText(DisplayText text)
    {
        var settings = VoiceLineSettings.Current;

        if (string.IsNullOrEmpty(text.English))
            return BuildSizedLine(text.Chinese, settings.ChineseFontScale);
        if (string.IsNullOrEmpty(text.Chinese))
            return BuildSizedLine(text.English, settings.EnglishFontScale);

        return $"{BuildSizedLine(text.English, settings.EnglishFontScale)}\n"
            + BuildSizedLine(text.Chinese, ResolveEffectiveChineseFontScale(settings, text));
    }

    private static string BuildSizedLine(string value, float scale)
    {
        var percent = (int)Math.Round(scale * 100f);
        return $"<size={percent}%>{value}</size>";
    }

    private static float ResolveEffectiveChineseFontScale(
        VoiceLineSettings settings,
        DisplayText? currentText
    )
    {
        if (
            currentText.HasValue
            && currentText.Value.HasEnglishAndChinese
            && AreScalesEquivalent(settings.EnglishFontScale, settings.ChineseFontScale)
        )
        {
            // When bilingual subtitles use the same configured scale, Chinese looks
            // slightly smaller next to the Latin line; a tiny render-only multiplier
            // keeps the two rows visually balanced without changing the saved setting.
            return settings.EnglishFontScale * BilingualChineseScaleMultiplier;
        }

        return settings.ChineseFontScale;
    }

    internal static string ConvertCenteredChineseTrailingPunctuation(
        string chinese,
        SubtitlePosition position
    )
    {
        if (position != SubtitlePosition.TopCenter || string.IsNullOrEmpty(chinese))
            return chinese;

        var punctuationIndex = chinese.Length - 1;
        while (punctuationIndex >= 0 && IsClosingQuote(chinese[punctuationIndex]))
            punctuationIndex--;

        if (punctuationIndex < 0)
            return chinese;

        var halfwidthPunctuation = chinese[punctuationIndex] switch
        {
            '。' => '｡', // U+3002 IDEOGRAPHIC FULL STOP -> U+FF61 HALFWIDTH IDEOGRAPHIC FULL STOP
            '！' => '!',
            '？' => '?',
            _ => '\0',
        };
        if (halfwidthPunctuation == '\0')
            return chinese;

        // This is display-only. Let TMP center the actual narrower punctuation advance
        // instead of shifting a whole Chinese row by a guessed fixed width.
        return chinese.Substring(0, punctuationIndex)
            + halfwidthPunctuation
            + chinese.Substring(punctuationIndex + 1);
    }

    private static bool IsClosingQuote(char value) => value is '」' or '』' or '”' or '’';

    private static bool AreScalesEquivalent(float left, float right) =>
        Math.Abs(left - right) <= ScaleComparisonTolerance;

    private static string? SafeAnchorPath(TextMeshProUGUI? label)
    {
        if (label == null)
            return null;

        try
        {
            return BuildPath(label.transform);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeAnchorText(TextMeshProUGUI? label)
    {
        if (label == null)
            return null;

        try
        {
            return label.text;
        }
        catch
        {
            return null;
        }
    }

    private static string SafePlaybackStateText(Func<string>? playbackStateText)
    {
        try
        {
            return playbackStateText?.Invoke() ?? "<none>";
        }
        catch
        {
            return "<error>";
        }
    }

    private static long ToMilliseconds(float seconds) =>
        (long)Math.Round(seconds * 1000f, MidpointRounding.AwayFromZero);

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

    private readonly struct DisplayText
    {
        public static readonly DisplayText Empty = new(string.Empty, string.Empty);

        public DisplayText(string english, string chinese)
        {
            English = english;
            Chinese = chinese;
        }

        public string English { get; }

        public string Chinese { get; }

        public bool HasEnglishAndChinese =>
            !string.IsNullOrEmpty(English) && !string.IsNullOrEmpty(Chinese);

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(English) && string.IsNullOrWhiteSpace(Chinese);
    }
}
