#nullable enable
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace BazaarPlusPlus.GameInterop.Fonts;

/// <summary>
/// Applies the game's native typography to every BPP-owned text surface while keeping renderer-
/// specific assets, fallback chains, coverage checks, and cleanup inside this adapter.
/// </summary>
internal static class NativeGameTypography
{
    private const string ChineseLocale = "zh-CN";
    private const string PanelTextSettingsName = "BPP Native Game Font Text Settings";
    private const int HealthKey = 0;

    private static readonly FieldInfo? OsFallbackFontAssetsField = typeof(TextSettings).GetField(
        "m_FallbackOSFontAssets",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );
    private static readonly FieldInfo? EmojiSupportField = typeof(TextSettings).GetField(
        "m_EnableEmojiSupport",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );
    private static readonly FieldInfo? EmojiFallbackTextAssetsField = typeof(TextSettings).GetField(
        "m_EmojiFallbackTextAssets",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    );
    private static readonly List<AsyncOperationHandle<TMP_FontAsset>> Handles = new();
    private static readonly Dictionary<object, TMP_FontAsset> LoadedByRuntimeKey = new();
    private static readonly List<FontBinding> Bindings = new();
    private static readonly List<PanelScope> PanelScopes = new();
    private static readonly OperationalHealthTracker<int, NativeGameFontReasonCode> Health = new();

    private static TMP_FontAsset[]? _serifFallbacks;
    private static TMP_FontAsset[]? _sansFallbacks;
    private static TMP_FontAsset? _serifFontAsset;
    private static TMP_FontAsset? _sansFontAsset;
    private static TMP_FontAsset? _sansDynamicFontAsset;
    private static Font? _sansSourceFont;
    private static bool _readyReported;
    private static int _generation;
    private static int _mainThreadId;

    internal enum Outcome
    {
        Ready,
        Applied,
        NotNeeded,
        Waiting,
        Unavailable,
    }

    internal enum OwnedTextRole
    {
        Body,
        Heading,
    }

    internal enum ExternalTextSupport
    {
        Supported,
        Unsupported,
        Unavailable,
    }

    internal static void InitializeForCurrentThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        var installedThreadId = Interlocked.CompareExchange(
            ref _mainThreadId,
            currentThreadId,
            comparand: 0
        );
        if (installedThreadId != 0 && installedThreadId != currentThreadId)
            throw new InvalidOperationException(
                "Native game typography was initialized from a different thread."
            );
    }

    internal static Outcome PrepareOwnedText(out OwnedTextPreparation? preparation) =>
        PrepareOwnedText(OwnedTextRole.Body, out preparation);

    internal static Outcome PrepareOwnedText(
        OwnedTextRole role,
        out OwnedTextPreparation? preparation
    )
    {
        EnsureMainThread();
        TMP_FontAsset? fontAsset;
        var ready = role switch
        {
            OwnedTextRole.Body => TryGetSansFontAsset(out fontAsset),
            OwnedTextRole.Heading => TryGetSerifFontAsset(out fontAsset),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        if (ready && fontAsset != null)
        {
            preparation = new OwnedTextPreparation(fontAsset, _generation);
            return Outcome.Ready;
        }

        preparation = null;
        return NotoFontFallbackRuntime.HasConfiguration ? Outcome.Unavailable : Outcome.Waiting;
    }

    internal static Outcome EnsureNativeTextCoverage(TMP_Text text, string? sampleText)
    {
        EnsureMainThread();
        if (text == null)
            throw new ArgumentNullException(nameof(text));
        if (text.font == null)
            return Outcome.Unavailable;
        if (string.IsNullOrWhiteSpace(sampleText) || !UnicodeFontCoverage.ContainsCjk(sampleText))
            return Outcome.NotNeeded;

        var preferSerif = text.font.name.IndexOf("Serif", StringComparison.OrdinalIgnoreCase) >= 0;
        var attempt = ResolveFallbacks(preferSerif);
        if (attempt.Fonts.Length == 0 && preferSerif)
        {
            var sansAttempt = ResolveFallbacks(preferSerif: false);
            attempt =
                sansAttempt.Fonts.Length > 0 ? sansAttempt : attempt.MergeFailure(sansAttempt);
        }
        Observe(attempt);
        if (attempt.Fonts.Length == 0)
            return attempt.WasAttempted ? Outcome.Unavailable : Outcome.Waiting;

        var binding = FindOrCreateBinding(text);
        if (binding?.Clone?.fallbackFontAssetTable == null)
            return Outcome.Unavailable;

        var installed = false;
        foreach (var fallback in attempt.Fonts)
        {
            if (fallback == null || binding.Clone.fallbackFontAssetTable.Contains(fallback))
                continue;

            binding.Clone.fallbackFontAssetTable.Add(fallback);
            installed = true;
        }

        if (installed)
            text.ForceMeshUpdate(ignoreActiveState: true);
        return Outcome.Applied;
    }

    private static bool TryGetSansSourceFont(out Font? sourceFont) =>
        TryGetSourceFont(
            preferSerif: false,
            ref _sansDynamicFontAsset,
            ref _sansSourceFont,
            out sourceFont
        );

    private static bool TryGetSansFontAsset(out TMP_FontAsset? fontAsset) =>
        TryGetOwnedFontAsset(
            NotoFontFallbackRuntime._loadedSansPrimary,
            preferSerif: false,
            "BPP Game Sans",
            ref _sansFontAsset,
            out fontAsset
        );

    private static bool TryGetSerifFontAsset(out TMP_FontAsset? fontAsset) =>
        TryGetOwnedFontAsset(
            NotoFontFallbackRuntime._loadedSerifPrimary,
            preferSerif: true,
            "BPP Game Serif",
            ref _serifFontAsset,
            out fontAsset
        );

    private static bool TryGetOwnedFontAsset(
        TMP_FontAsset? primary,
        bool preferSerif,
        string cloneSuffix,
        ref TMP_FontAsset? cachedFontAsset,
        out TMP_FontAsset? fontAsset
    )
    {
        if (cachedFontAsset != null)
        {
            fontAsset = cachedFontAsset;
            return true;
        }

        if (primary == null)
        {
            if (NotoFontFallbackRuntime.HasConfiguration)
                ReportFailure(
                    NativeGameFontStage.ResolveSourceFont,
                    NativeGameFontReasonCode.SourceFontUnavailable,
                    null
                );
            fontAsset = null;
            return false;
        }

        var attempt = ResolveFallbacks(preferSerif);
        Observe(attempt);
        if (attempt.Fonts.Length == 0)
        {
            fontAsset = null;
            return false;
        }

        try
        {
            var clone = Object.Instantiate(primary);
            clone.name = $"{primary.name} ({cloneSuffix})";
            var fallbackFontAssets = new List<TMP_FontAsset>(
                primary.fallbackFontAssetTable ?? new List<TMP_FontAsset>()
            );
            foreach (var fallback in attempt.Fonts)
                if (fallback != null && !fallbackFontAssets.Contains(fallback))
                    fallbackFontAssets.Add(fallback);
            clone.fallbackFontAssetTable = fallbackFontAssets;
            cachedFontAsset = clone;
            fontAsset = clone;
            return true;
        }
        catch (Exception ex)
        {
            ReportFailure(
                NativeGameFontStage.ResolveSourceFont,
                NativeGameFontReasonCode.SourceFontUnavailable,
                ex
            );
            fontAsset = null;
            return false;
        }
    }

    private static bool TryGetSourceFont(
        bool preferSerif,
        ref TMP_FontAsset? cachedDynamicFontAsset,
        ref Font? cachedSourceFont,
        out Font? sourceFont
    )
    {
        if (cachedSourceFont != null)
        {
            sourceFont = cachedSourceFont;
            return true;
        }

        if (
            !TryGetDynamicFontAsset(
                preferSerif,
                ref cachedDynamicFontAsset,
                out var dynamicFontAsset
            )
            || dynamicFontAsset?.sourceFontFile == null
        )
        {
            sourceFont = null;
            return false;
        }

        cachedSourceFont = dynamicFontAsset.sourceFontFile;
        sourceFont = cachedSourceFont;
        return true;
    }

    private static bool TryGetDynamicFontAsset(
        bool preferSerif,
        ref TMP_FontAsset? cachedDynamicFontAsset,
        out TMP_FontAsset? fontAsset
    )
    {
        if (cachedDynamicFontAsset != null)
        {
            fontAsset = cachedDynamicFontAsset;
            return true;
        }

        var attempt = ResolveFallbacks(preferSerif);
        if (attempt.Fonts.Length == 0)
        {
            Observe(attempt);
            if (attempt.WasAttempted)
                ReportFailure(
                    NativeGameFontStage.ResolveSourceFont,
                    NativeGameFontReasonCode.SourceFontUnavailable,
                    null
                );
            fontAsset = null;
            return false;
        }
        var sourceIndex = NativeGameFontSelection.FindLastIndexWithSource(
            attempt.Fonts,
            candidate =>
                candidate != null
                && candidate.atlasPopulationMode == TMPro.AtlasPopulationMode.Dynamic
                && candidate.sourceFontFile != null
        );
        if (sourceIndex >= 0)
        {
            var candidate = attempt.Fonts[sourceIndex];
            if (!candidate.sourceFontFile.dynamic)
            {
                ReportFailure(
                    NativeGameFontStage.ResolveSourceFont,
                    NativeGameFontReasonCode.SourceFontNotDynamic,
                    null
                );
                fontAsset = null;
                return false;
            }

            cachedDynamicFontAsset = candidate;
            fontAsset = candidate;
            ReportSuccess(attempt.Fonts, candidate.sourceFontFile);
            return true;
        }

        ReportFailure(
            NativeGameFontStage.ResolveSourceFont,
            NativeGameFontReasonCode.SourceFontUnavailable,
            null
        );
        fontAsset = null;
        return false;
    }

    private static bool TryFindMissingCodePoint(Font? font, string? text, out int missingCodePoint)
    {
        if (font == null)
        {
            missingCodePoint = 0;
            return !string.IsNullOrEmpty(text);
        }

        return UnicodeFontCoverage.TryFindMissingCodePoint(
            text,
            font.HasCharacter,
            out missingCodePoint
        );
    }

    private static ExternalTextSupport CheckExternalText(Font? font, string? text, string surface)
    {
        if (!TryFindMissingCodePoint(font, text, out var missingCodePoint))
            return ExternalTextSupport.Supported;

        BppLog.WarnEvent(
            NativeGameFontsLogEvents.TextRejected,
            NativeGameFontsLogEvents.TextRejectedSurface.Bind(surface),
            NativeGameFontsLogEvents.TextRejectedCodePoint.Bind($"U+{missingCodePoint:X}")
        );
        return ExternalTextSupport.Unsupported;
    }

    internal static Outcome TryAttachPanel(PanelSettings panelSettings, out PanelScope? scope)
    {
        EnsureMainThread();
        if (panelSettings == null)
            throw new ArgumentNullException(nameof(panelSettings));
        scope = null;
        if (panelSettings.textSettings != null)
            return Outcome.Unavailable;
        if (!TryGetSansSourceFont(out var bodyFont) || bodyFont == null)
            return NotoFontFallbackRuntime.HasConfiguration ? Outcome.Unavailable : Outcome.Waiting;

        PanelTextSettings? textSettings = null;
        PanelScope? createdScope = null;
        try
        {
            RequireField(OsFallbackFontAssetsField, "m_FallbackOSFontAssets");
            RequireField(EmojiSupportField, "m_EnableEmojiSupport");
            RequireField(EmojiFallbackTextAssetsField, "m_EmojiFallbackTextAssets");

            textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            textSettings.name = PanelTextSettingsName;
            textSettings.defaultFontAsset = null;
            textSettings.fallbackFontAssets = new List<TextCoreFontAsset>();
            textSettings.missingCharacterUnicode = 0x25A1;
            EmojiSupportField!.SetValue(textSettings, false);
            SetEmptyCollection(textSettings, EmojiFallbackTextAssetsField!);
            SetEmptyCollection(textSettings, OsFallbackFontAssetsField!);

            if (!IsIsolated(textSettings))
                throw new InvalidOperationException(
                    "Panel text settings retain a fallback font path."
                );

            createdScope = new PanelScope(panelSettings, textSettings, bodyFont, _generation);
            PanelScopes.Add(createdScope);
            panelSettings.textSettings = textSettings;
            scope = createdScope;
            return Outcome.Ready;
        }
        catch (Exception ex)
        {
            if (createdScope != null)
                PanelScopes.Remove(createdScope);
            if (textSettings != null)
                Object.DestroyImmediate(textSettings);
            scope = null;
            ReportFailure(
                NativeGameFontStage.ConfigurePanelTextSettings,
                NativeGameFontReasonCode.PanelTextSettingsUnavailable,
                ex
            );
            return Outcome.Unavailable;
        }
    }

    internal static bool IsIsolated(PanelTextSettings? textSettings)
    {
        if (
            textSettings == null
            || OsFallbackFontAssetsField == null
            || EmojiSupportField == null
            || EmojiFallbackTextAssetsField == null
        )
            return false;

        var osFallbacks = OsFallbackFontAssetsField.GetValue(textSettings) as ICollection;
        var emojiFallbacks = EmojiFallbackTextAssetsField.GetValue(textSettings) as ICollection;
        var emojiSupport = EmojiSupportField.GetValue(textSettings) as bool?;
        return textSettings.defaultFontAsset == null
            && (textSettings.fallbackFontAssets?.Count ?? 0) == 0
            && emojiSupport == false
            && emojiFallbacks?.Count == 0
            && osFallbacks?.Count == 0;
    }

    private static void RequireField(FieldInfo? field, string name)
    {
        if (field == null)
            throw new MissingFieldException(typeof(TextSettings).FullName, name);
    }

    private static void SetEmptyCollection(TextSettings target, FieldInfo field)
    {
        var empty = Activator.CreateInstance(field.FieldType);
        if (empty is not ICollection collection || collection.Count != 0)
            throw new InvalidOperationException($"{field.Name} is not an empty collection.");

        field.SetValue(target, empty);
        if (field.GetValue(target) is not ICollection installed || installed.Count != 0)
            throw new InvalidOperationException(
                $"{field.Name} did not retain an empty collection."
            );
    }

    internal static void Reset()
    {
        EnsureMainThread();
        try
        {
            RestoreBindings();
            foreach (var scope in PanelScopes.ToArray())
                scope.DisposeFromReset();
            PanelScopes.Clear();
            if (_serifFontAsset != null)
                Object.DestroyImmediate(_serifFontAsset);
            if (_sansFontAsset != null)
                Object.DestroyImmediate(_sansFontAsset);
        }
        finally
        {
            foreach (var handle in Handles)
                TryRelease(handle);
            Handles.Clear();
            LoadedByRuntimeKey.Clear();
        }

        _serifFallbacks = null;
        _sansFallbacks = null;
        _serifFontAsset = null;
        _sansFontAsset = null;
        _sansDynamicFontAsset = null;
        _sansSourceFont = null;
        Health.Reset();
        _readyReported = false;
        _generation++;
    }

    private static void RestoreBindings()
    {
        foreach (var binding in Bindings)
        {
            try
            {
                if (
                    binding.Text != null
                    && binding.Clone != null
                    && binding.Text.font == binding.Clone
                    && binding.Original != null
                )
                    binding.Text.font = binding.Original;
            }
            catch (Exception ex)
            {
                BppLog.DebugEvent(
                    NativeGameFontsLogEvents.CleanupFailed,
                    ex,
                    () =>
                        [
                            NativeGameFontsLogEvents.CleanupFailedStage.Bind(
                                NativeGameFontStage.RestoreBinding
                            ),
                        ]
                );
            }
            finally
            {
                if (binding.Clone != null)
                    Object.DestroyImmediate(binding.Clone);
            }
        }
        Bindings.Clear();
    }

    private static FontBinding? FindOrCreateBinding(TMP_Text text)
    {
        CleanupDeadBindings();
        foreach (var binding in Bindings)
            if (binding.Text == text)
                return binding;

        var primary = text.font;
        if (primary == null)
            return null;

        var clone = Object.Instantiate(primary);
        clone.name = $"{primary.name} (BPP Game Font Fallback)";
        clone.fallbackFontAssetTable = new List<TMP_FontAsset>(
            primary.fallbackFontAssetTable ?? new List<TMP_FontAsset>()
        );
        text.font = clone;

        var created = new FontBinding(text, primary, clone);
        Bindings.Add(created);
        return created;
    }

    private static void CleanupDeadBindings()
    {
        for (var index = Bindings.Count - 1; index >= 0; index--)
        {
            var binding = Bindings[index];
            if (binding.Text != null && binding.Clone != null)
                continue;

            if (binding.Clone != null)
                Object.DestroyImmediate(binding.Clone);
            Bindings.RemoveAt(index);
        }
    }

    private static FontLoadAttempt ResolveFallbacks(bool preferSerif)
    {
        var cached = preferSerif ? _serifFallbacks : _sansFallbacks;
        if (cached != null)
            return FontLoadAttempt.Cached(cached);

        if (!NotoFontFallbackRuntime.HasConfiguration)
            return FontLoadAttempt.NotReady();

        var configuration = NotoFontFallbackRuntime._configuration;
        if (
            configuration == null
            || !configuration.TryGetRuleForLocale(ChineseLocale, out var rule)
        )
        {
            return FontLoadAttempt.Failed(
                NativeGameFontStage.ResolveConfiguration,
                NativeGameFontReasonCode.ConfigurationUnavailable
            );
        }

        var references = preferSerif
            ? rule.NotoSerifFallbacksOrdered
            : rule.NotoSansFallbacksOrdered;
        var attempt = LoadReferences(references);
        if (attempt.Fonts.Length > 0)
        {
            if (preferSerif)
                _serifFallbacks = attempt.Fonts;
            else
                _sansFallbacks = attempt.Fonts;
        }
        return attempt;
    }

    private static FontLoadAttempt LoadReferences(AssetReferenceT<TMP_FontAsset>[]? references)
    {
        if (references == null || references.Length == 0)
            return FontLoadAttempt.Failed(
                NativeGameFontStage.LoadFonts,
                NativeGameFontReasonCode.FontReferencesUnavailable
            );

        var loaded = new List<TMP_FontAsset>(references.Length);
        Exception? firstException = null;
        var sawLoadFailure = false;
        var sawValidReference = false;
        foreach (var reference in references)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
                continue;
            sawValidReference = true;
            var runtimeKey = reference.RuntimeKey;
            if (LoadedByRuntimeKey.TryGetValue(runtimeKey, out var cachedFont))
            {
                loaded.Add(cachedFont);
                continue;
            }

            AsyncOperationHandle<TMP_FontAsset> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<TMP_FontAsset>(runtimeKey);
                var font = handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded || font == null)
                {
                    sawLoadFailure = true;
                    continue;
                }

                font.ReadFontAssetDefinition();
                loaded.Add(font);
                LoadedByRuntimeKey.Add(runtimeKey, font);
                Handles.Add(handle);
                handle = default;
            }
            catch (Exception ex)
            {
                sawLoadFailure = true;
                firstException ??= ex;
            }
            finally
            {
                TryRelease(handle);
            }
        }

        if (NativeGameFontSelection.HasCompleteChain(references.Length, loaded.Count))
            return FontLoadAttempt.Succeeded(loaded.ToArray());
        return FontLoadAttempt.Failed(
            NativeGameFontStage.LoadFonts,
            sawLoadFailure
                ? NativeGameFontReasonCode.FontLoadFailed
                : NativeGameFontReasonCode.FontReferencesUnavailable,
            firstException,
            wasAttempted: sawValidReference || references.Length > 0
        );
    }

    private static void TryRelease(AsyncOperationHandle<TMP_FontAsset> handle)
    {
        try
        {
            if (handle.IsValid())
                Addressables.Release(handle);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                NativeGameFontsLogEvents.CleanupFailed,
                ex,
                () =>
                    [
                        NativeGameFontsLogEvents.CleanupFailedStage.Bind(
                            NativeGameFontStage.ReleaseHandle
                        ),
                    ]
            );
        }
    }

    private static void Observe(FontLoadAttempt attempt)
    {
        if (!attempt.WasAttempted)
            return;
        if (attempt.Fonts.Length > 0)
            ReportSuccess(attempt.Fonts, null);
        else
            ReportFailure(attempt.Stage, attempt.ReasonCode, attempt.Exception);
    }

    private static void ReportFailure(
        NativeGameFontStage stage,
        NativeGameFontReasonCode reasonCode,
        Exception? exception
    )
    {
        _readyReported = false;
        if (!Health.ObserveFailure(HealthKey, reasonCode))
            return;
        var fields = new[]
        {
            NativeGameFontsLogEvents.DegradedStage.Bind(stage),
            NativeGameFontsLogEvents.DegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(NativeGameFontsLogEvents.Degraded, fields);
        else
            BppLog.WarnEvent(NativeGameFontsLogEvents.Degraded, exception, fields);
    }

    private static void ReportSuccess(TMP_FontAsset[] fonts, Font? sourceFont)
    {
        if (Health.ObserveSuccess(HealthKey, out _))
        {
            _readyReported = true;
            BppLog.RecoverStorm(NativeGameFontsLogEvents.Degraded);
            BppLog.InfoEvent(
                NativeGameFontsLogEvents.Recovered,
                NativeGameFontsLogEvents.RecoveredFontCount.Bind(fonts.Length)
            );
            return;
        }

        if (_readyReported)
            return;
        _readyReported = true;
        BppLog.DebugEvent(
            NativeGameFontsLogEvents.Loaded,
            () =>
                [
                    NativeGameFontsLogEvents.LoadedFontCount.Bind(fonts.Length),
                    NativeGameFontsLogEvents.LoadedFontNames.Bind(
                        string.Join(",", Array.ConvertAll(fonts, font => font.name))
                    ),
                    NativeGameFontsLogEvents.LoadedSourceFont.Bind(sourceFont?.name),
                ]
        );
    }

    private static void EnsureMainThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (_mainThreadId == 0)
            throw new InvalidOperationException(
                "Native game typography must be initialized on the Unity main thread."
            );
        if (_mainThreadId != currentThreadId)
            throw new InvalidOperationException(
                "Native game typography can only be used on the Unity main thread."
            );
    }

    internal sealed class OwnedTextPreparation
    {
        private readonly TMP_FontAsset _fontAsset;
        private readonly int _preparedGeneration;

        internal OwnedTextPreparation(TMP_FontAsset fontAsset, int preparedGeneration)
        {
            _fontAsset = fontAsset;
            _preparedGeneration = preparedGeneration;
        }

        internal Outcome Apply(TMP_Text text)
        {
            EnsureMainThread();
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            if (_preparedGeneration != _generation || _fontAsset == null)
                return Outcome.Unavailable;

            text.font = _fontAsset;
            return Outcome.Applied;
        }
    }

    internal sealed class PanelScope : IDisposable
    {
        private readonly PanelSettings _panelSettings;
        private readonly PanelTextSettings _textSettings;
        private readonly Font _bodyFont;
        private readonly int _attachedGeneration;
        private bool _disposed;

        internal PanelScope(
            PanelSettings panelSettings,
            PanelTextSettings textSettings,
            Font bodyFont,
            int attachedGeneration
        )
        {
            _panelSettings = panelSettings;
            _textSettings = textSettings;
            _bodyFont = bodyFont;
            _attachedGeneration = attachedGeneration;
        }

        internal Outcome Apply(VisualElement element)
        {
            EnsureMainThread();
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (_disposed || _attachedGeneration != _generation)
                return Outcome.Unavailable;

            element.style.unityFont = _bodyFont;
            element.style.unityFontDefinition = FontDefinition.FromFont(_bodyFont);
            return Outcome.Applied;
        }

        internal ExternalTextSupport CheckExternalText(string? text, string surface)
        {
            EnsureMainThread();
            if (string.IsNullOrWhiteSpace(surface))
                throw new ArgumentException("A diagnostic surface is required.", nameof(surface));
            if (_disposed || _attachedGeneration != _generation)
                return ExternalTextSupport.Unavailable;

            return NativeGameTypography.CheckExternalText(_bodyFont, text, surface);
        }

        public void Dispose()
        {
            EnsureMainThread();
            DisposeCore(immediate: false);
        }

        internal void DisposeFromReset()
        {
            DisposeCore(immediate: true);
        }

        private void DisposeCore(bool immediate)
        {
            if (_disposed)
                return;
            _disposed = true;
            PanelScopes.Remove(this);

            if (_panelSettings != null && _panelSettings.textSettings == _textSettings)
                _panelSettings.textSettings = null;
            if (_textSettings == null)
                return;
            if (immediate)
                Object.DestroyImmediate(_textSettings);
            else
                Object.Destroy(_textSettings);
        }
    }

    private readonly struct FontLoadAttempt
    {
        private FontLoadAttempt(
            TMP_FontAsset[] fonts,
            bool wasAttempted,
            NativeGameFontStage stage,
            NativeGameFontReasonCode reasonCode,
            Exception? exception
        )
        {
            Fonts = fonts;
            WasAttempted = wasAttempted;
            Stage = stage;
            ReasonCode = reasonCode;
            Exception = exception;
        }

        internal TMP_FontAsset[] Fonts { get; }
        internal bool WasAttempted { get; }
        internal NativeGameFontStage Stage { get; }
        internal NativeGameFontReasonCode ReasonCode { get; }
        internal Exception? Exception { get; }

        internal static FontLoadAttempt Cached(TMP_FontAsset[] fonts) =>
            new(fonts, false, default, default, null);

        internal static FontLoadAttempt Succeeded(TMP_FontAsset[] fonts) =>
            new(fonts, true, default, default, null);

        internal static FontLoadAttempt NotReady() =>
            new(
                Array.Empty<TMP_FontAsset>(),
                false,
                NativeGameFontStage.ResolveConfiguration,
                NativeGameFontReasonCode.ConfigurationUnavailable,
                null
            );

        internal static FontLoadAttempt Failed(
            NativeGameFontStage stage,
            NativeGameFontReasonCode reasonCode,
            Exception? exception = null,
            bool wasAttempted = true
        ) => new(Array.Empty<TMP_FontAsset>(), wasAttempted, stage, reasonCode, exception);

        internal FontLoadAttempt MergeFailure(FontLoadAttempt later) =>
            later.WasAttempted ? later : this;
    }

    private sealed class FontBinding
    {
        internal FontBinding(TMP_Text text, TMP_FontAsset original, TMP_FontAsset clone)
        {
            Text = text;
            Original = original;
            Clone = clone;
        }

        internal TMP_Text Text { get; }
        internal TMP_FontAsset Original { get; }
        internal TMP_FontAsset Clone { get; }
    }
}
