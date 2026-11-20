#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Native CardPreviewItem.LoadArt bypasses AssetLoader and never releases the Addressables references.
internal sealed class CollectionCardArtCache
{
    private const int DefaultCapacity = 256;

    private readonly int _capacity;
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodeMap = new(
        StringComparer.Ordinal
    );
    private readonly HashSet<string> _failedKeys = new(StringComparer.Ordinal);

    public CollectionCardArtCache(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(8, capacity);
    }

    // Returns null on load failure; the caller proceeds without a material assignment, which
    // matches the game's own behaviour for invalid art keys (blank face, frame still drawn).
    public Task<CardAssetDataSO?> Get(string artKey) => GetCore(artKey, acquireRef: false);

    public Task<CardAssetDataSO?> Acquire(string artKey) => GetCore(artKey, acquireRef: true);

    internal void ReportDegraded(
        CollectionPanelLogReasonCode reasonCode,
        string? artKey,
        Exception exception
    ) => ReportArtDegraded(reasonCode, artKey, exception);

    private static void ReportArtDegraded(
        CollectionPanelLogReasonCode reasonCode,
        string? artKey,
        Exception? exception
    )
    {
        var degraded = new BppLogEvent(
            BppLogFeatureScope.CollectionPanel,
            "collection_panel.card_art.degraded",
            storm: ["reason_code"]
        );
        var fields = new BppLogField[]
        {
            ("reason_code", reasonCode),
            ("status", CollectionCardArtStatus.ArtUnavailable),
            ("art_key", artKey),
        };
        if (exception == null)
            BppLog.WarnEvent(degraded, fields);
        else
            BppLog.WarnEvent(degraded, exception, fields);
    }

    private async Task<CardAssetDataSO?> GetCore(string artKey, bool acquireRef)
    {
        if (string.IsNullOrEmpty(artKey))
            return null;

        if (_failedKeys.Contains(artKey))
            return null;

        if (_entries.TryGetValue(artKey, out var existing))
        {
            Touch(artKey);
            if (acquireRef)
                existing.RefCount++;
            return existing.Asset;
        }

        AsyncOperationHandle<CardAssetDataSO> handle;
        try
        {
            handle = Addressables.LoadAssetAsync<CardAssetDataSO>(artKey);
            await handle.Task;
        }
        catch (Exception ex)
        {
            ReportArtDegraded(CollectionPanelLogReasonCode.AddressablesLoadException, artKey, ex);
            _failedKeys.Add(artKey);
            return null;
        }

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            ReportArtDegraded(CollectionPanelLogReasonCode.AddressablesLoadFailed, artKey, null);
            try
            {
                Addressables.Release(handle);
            }
            catch { }
            _failedKeys.Add(artKey);
            return null;
        }

        // A second concurrent Get could have populated the entry while we awaited.
        if (_entries.TryGetValue(artKey, out var raced))
        {
            try
            {
                Addressables.Release(handle);
            }
            catch { }
            Touch(artKey);
            if (acquireRef)
                raced.RefCount++;
            return raced.Asset;
        }

        var node = _lru.AddFirst(artKey);
        _nodeMap[artKey] = node;
        _entries[artKey] = new CacheEntry(handle, handle.Result, acquireRef ? 1 : 0);
        Evict();
        return handle.Result;
    }

    public void Release(string artKey)
    {
        if (string.IsNullOrEmpty(artKey))
            return;
        if (_entries.TryGetValue(artKey, out var entry))
            entry.RefCount = Math.Max(0, entry.RefCount - 1);
    }

    public void DisposeAll()
    {
        foreach (var pair in _entries)
        {
            try
            {
                Addressables.Release(pair.Value.Handle);
            }
            catch (Exception ex)
            {
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CollectionPanel,
                        "collection_panel.cache.cleanup_failed"
                    ),
                    ex,
                    () =>
                        [
                            ("cache", CollectionCacheKind.Art),
                            ("stage", CollectionCacheCleanupStage.Release),
                            ("art_key", pair.Key),
                        ]
                );
            }
        }
        _entries.Clear();
        _lru.Clear();
        _nodeMap.Clear();
        _failedKeys.Clear();
    }

    private void Evict()
    {
        while (_lru.Count > _capacity)
        {
            // The cache may exceed capacity while every handle is retained by active or pooled cards.
            var node = _lru.Last;
            string? evictKey = null;
            while (node != null)
            {
                if (_entries.TryGetValue(node.Value, out var entry) && entry.RefCount <= 0)
                {
                    evictKey = node.Value;
                    break;
                }
                node = node.Previous;
            }
            if (evictKey == null)
                break;

            if (_nodeMap.TryGetValue(evictKey, out var evictNode))
            {
                _lru.Remove(evictNode);
                _nodeMap.Remove(evictKey);
            }
            if (_entries.TryGetValue(evictKey, out var evictedEntry))
            {
                _entries.Remove(evictKey);
                try
                {
                    Addressables.Release(evictedEntry.Handle);
                }
                catch (Exception ex)
                {
                    BppLog.DebugEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.CollectionPanel,
                            "collection_panel.cache.cleanup_failed"
                        ),
                        ex,
                        () =>
                            [
                                ("cache", CollectionCacheKind.Art),
                                ("stage", CollectionCacheCleanupStage.EvictRelease),
                                ("art_key", evictKey),
                            ]
                    );
                }
            }
        }
    }

    private void Touch(string artKey)
    {
        if (!_nodeMap.TryGetValue(artKey, out var node))
            return;
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private sealed class CacheEntry
    {
        public CacheEntry(
            AsyncOperationHandle<CardAssetDataSO> handle,
            CardAssetDataSO asset,
            int refCount
        )
        {
            Handle = handle;
            Asset = asset;
            RefCount = Math.Max(0, refCount);
        }

        public AsyncOperationHandle<CardAssetDataSO> Handle { get; }
        public CardAssetDataSO Asset { get; }
        public int RefCount { get; set; }
    }
}
