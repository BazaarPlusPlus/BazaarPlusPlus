using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.Localization;
using Il2CppTMPro;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The font every BPP Android surface draws with: a BPP-owned copy of the main-menu version
/// label's font. The game loads its Noto CJK fallbacks only for a CJK language, so in any other
/// language the label's font draws a Chinese name as empty boxes. The first text a surface hands
/// <see cref="Cover"/> that holds a CJK character loads the fallbacks of the game's own
/// <c>zh-CN</c> rule (<c>NotoFontFallbackRuntime</c>'s configuration), as the desktop's
/// <c>NativeGameTypography</c> does, and appends them to the copy's chain only, so the game's
/// own text is untouched. A dynamic source keeps its atlas: the copy is static and asks the
/// source first for any character it lacks. Main thread only.
/// </summary>
internal static class AndroidGameFont
{
    private const string ChineseLocale = "zh-CN";

    private static TMP_FontAsset? _source;
    private static TMP_FontAsset? _copy;
    private static State _state;
    private static readonly List<AsyncOperationHandle<TMP_FontAsset>> Handles = new();
    private static readonly List<TMP_Text> Waiting = new();

    private enum State
    {
        Idle,
        Loading,
        Ready,
        Failed,
    }

    /// <summary>The copy of the version label's font; null before the main menu has shown it.</summary>
    public static TMP_FontAsset? Font
    {
        get
        {
            if (Alive(_copy))
                return _copy;
            var source = VersionLabelPatch.Font;
            if (source == null)
                return null;
            _source = source;
            _copy = Copy(source);
            return _copy;
        }
    }

    /// <summary>
    /// A surface set <paramref name="label"/>'s text: when it holds a CJK character the copy
    /// cannot draw yet, the fallbacks start loading and the label redraws once they arrive.
    /// </summary>
    public static void Cover(TMP_Text label)
    {
        if (label == null || _state is State.Ready or State.Failed || !ContainsCjk(label.text))
            return;
        if (!Waiting.Contains(label))
            Waiting.Add(label);
        if (_state == State.Idle)
            Start();
    }

    /// <summary>Main thread, every frame: joins the fallbacks to the copy once they all loaded.</summary>
    public static void Tick()
    {
        if (_state != State.Loading)
            return;
        foreach (var handle in Handles)
            if (!handle.IsDone)
                return;
        Finish();
    }

    /// <summary>The host stops: the loaded fallbacks go back to Addressables and the copy goes.</summary>
    public static void Dispose()
    {
        foreach (var handle in Handles)
            Release(handle);
        Handles.Clear();
        Waiting.Clear();
        if (Alive(_copy))
            Object.Destroy(_copy);
        _copy = null;
        _source = null;
        _state = State.Idle;
    }

    private static TMP_FontAsset Copy(TMP_FontAsset source)
    {
        var copy = Object.Instantiate(source);
        copy.name = source.name + " (BPP)";
        // Only managed code holds the copy, so a scene load's unused-asset sweep would destroy it
        // under the panels that took it when they were built.
        copy.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
        var chain = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
        if (source.atlasPopulationMode == AtlasPopulationMode.Dynamic)
        {
            // Two assets adding glyphs to one atlas would overwrite each other's.
            copy.atlasPopulationMode = AtlasPopulationMode.Static;
            chain.Add(source);
        }
        var sourceChain = source.fallbackFontAssetTable;
        if (sourceChain != null)
            foreach (var fallback in sourceChain)
                if (fallback != null && !chain.Contains(fallback))
                    chain.Add(fallback);
        copy.fallbackFontAssetTable = chain;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.fonts.copied"),
            ("source", source.name),
            ("population", source.atlasPopulationMode.ToString()),
            ("chain", chain.Count)
        );
        return copy;
    }

    private static void Start()
    {
        _state = State.Loading;
        try
        {
            var configuration = NotoFontFallbackRuntime._configuration;
            if (configuration == null)
            {
                Fail("configuration_unavailable", null);
                return;
            }
            if (!configuration.TryGetRuleForLocale(ChineseLocale, out var rule) || rule == null)
            {
                Fail("rule_unavailable", null);
                return;
            }
            var serif =
                _source != null
                && _source.name.IndexOf("Serif", StringComparison.OrdinalIgnoreCase) >= 0;
            var references = serif ? rule.NotoSerifFallbacksOrdered : rule.NotoSansFallbacksOrdered;
            if (references == null || references.Length == 0)
            {
                Fail("references_unavailable", null);
                return;
            }
            foreach (var reference in references)
                if (reference != null && reference.RuntimeKeyIsValid())
                    Handles.Add(Addressables.LoadAssetAsync<TMP_FontAsset>(reference.RuntimeKey));
            if (Handles.Count == 0)
                Fail("references_unavailable", null);
        }
        catch (Exception exception)
        {
            Fail("load_failed", exception);
        }
    }

    private static void Finish()
    {
        try
        {
            var copy = _copy;
            if (!Alive(copy))
            {
                Fail("copy_gone", null);
                return;
            }
            var added = 0;
            foreach (var handle in Handles)
            {
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    Fail("load_failed", null);
                    return;
                }
                var font = handle.Result;
                font.ReadFontAssetDefinition();
                if (copy!.fallbackFontAssetTable.Contains(font))
                    continue;
                copy.fallbackFontAssetTable.Add(font);
                added++;
            }
            _state = State.Ready;
            var redrawn = 0;
            foreach (var label in Waiting)
                if (label != null && !label.WasCollected)
                {
                    label.ForceMeshUpdate(true, true);
                    redrawn++;
                }
            Waiting.Clear();
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.fonts.fallbacks_ready"),
                ("locale", ChineseLocale),
                ("fallbacks", added),
                ("redrawn", redrawn)
            );
        }
        catch (Exception exception)
        {
            Fail("load_failed", exception);
        }
    }

    // A failed load leaves the copy drawing what the label's font draws; no retry this session.
    private static void Fail(string reason, Exception? exception)
    {
        _state = State.Failed;
        Waiting.Clear();
        foreach (var handle in Handles)
            Release(handle);
        Handles.Clear();
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.Android,
            "android.fonts.fallbacks_failed"
        );
        if (exception == null)
            BppLog.WarnEvent(logEvent, ("reason", reason), ("locale", ChineseLocale));
        else
            BppLog.WarnEvent(logEvent, exception, ("reason", reason), ("locale", ChineseLocale));
    }

    private static void Release(AsyncOperationHandle<TMP_FontAsset> handle)
    {
        try
        {
            Addressables.Release(handle);
        }
        catch (Exception exception)
        {
            BppLog.DebugEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.fonts.release_failed"),
                exception,
                () => []
            );
        }
    }

    private static bool Alive(TMP_FontAsset? font) => font != null && !font.WasCollected;

    // The ranges the desktop's coverage check reads: CJK ideographs and their compatibility block.
    private static bool ContainsCjk(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var character in text)
            if (
                (character >= '㐀' && character <= '鿿') || (character >= '豈' && character <= '﫿')
            )
                return true;
        return false;
    }
}
