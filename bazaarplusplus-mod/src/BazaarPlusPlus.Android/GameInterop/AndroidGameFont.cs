using BazaarPlusPlus.Android.Patches;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.Localization;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Android.GameInterop;

// The game loads Noto CJK fallbacks only in CJK locales. Add them to the BPP copy to render
// mixed-language names.
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

    public static void Cover(TMP_Text label)
    {
        if (label == null || _state is State.Ready or State.Failed || !ContainsCjk(label.text))
            return;
        if (!Waiting.Contains(label))
            Waiting.Add(label);
        if (_state == State.Idle)
            Start();
    }

    public static void Tick()
    {
        if (_state != State.Loading)
            return;
        foreach (var handle in Handles)
            if (!handle.IsDone)
                return;
        Finish();
    }

    public static void Dispose()
    {
        foreach (var handle in Handles)
            Release(handle);
        Handles.Clear();
        Waiting.Clear();
        if (Alive(_copy))
        {
            // TMP_FontAsset.OnDestroy destroys its material and atlas textures, and the copy
            // shares both with the game's font.
            _copy!.material = null;
            _copy.atlasTextures = null;
            Object.Destroy(_copy);
        }
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
