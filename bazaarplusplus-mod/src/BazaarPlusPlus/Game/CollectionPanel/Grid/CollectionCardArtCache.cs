#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// L2 of the design's four-layer cache stack (§7). Item art (CardAssetDataSO) is loaded by
// CardPreviewItem.LoadArt directly through Addressables.LoadAssetAsync<>, bypassing the
// game's shared AssetLoader cache; each call increments the Addressables ref count and the
// game never releases. Without L2, scrolling through 1146 Items would leak 1146 ref counts.
//
// We hold the AsyncOperationHandle ourselves so we can call Addressables.Release exactly
// once per artKey when the panel closes. An LRU bound caps resident SOs at a fixed number
// regardless of catalog size; the LRU only evicts entries with refcount 0 so handles still
// in use by active or pooled cards stay resident.
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
    public async Task<CardAssetDataSO?> Get(string artKey)
    {
        if (string.IsNullOrEmpty(artKey))
            return null;

        if (_failedKeys.Contains(artKey))
            return null;

        if (_entries.TryGetValue(artKey, out var existing))
        {
            Touch(artKey);
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
            BppLog.Warn(
                "CollectionCardArtCache",
                $"Addressables.LoadAssetAsync threw for artKey='{artKey}': {ex.Message}"
            );
            _failedKeys.Add(artKey);
            return null;
        }

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            BppLog.Warn(
                "CollectionCardArtCache",
                $"Addressables load failed for artKey='{artKey}' status={handle.Status}."
            );
            try
            {
                Addressables.Release(handle);
            }
            catch
            {
                // best-effort
            }
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
            catch
            {
                // best-effort
            }
            Touch(artKey);
            return raced.Asset;
        }

        var node = _lru.AddFirst(artKey);
        _nodeMap[artKey] = node;
        _entries[artKey] = new CacheEntry(handle, handle.Result);
        Evict();
        return handle.Result;
    }

    public void AddRef(string artKey)
    {
        if (string.IsNullOrEmpty(artKey))
            return;
        if (_entries.TryGetValue(artKey, out var entry))
            entry.RefCount++;
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
                BppLog.Debug(
                    "CollectionCardArtCache",
                    $"Release failed for artKey='{pair.Key}': {ex.Message}"
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
            // Walk from tail to head looking for an entry with refcount 0. If every entry is
            // still in use we stop and let the cache temporarily exceed _capacity rather than
            // pull the rug from under a live card.
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
                    BppLog.Debug(
                        "CollectionCardArtCache",
                        $"Evict Release failed for artKey='{evictKey}': {ex.Message}"
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
        public CacheEntry(AsyncOperationHandle<CardAssetDataSO> handle, CardAssetDataSO asset)
        {
            Handle = handle;
            Asset = asset;
            RefCount = 0;
        }

        public AsyncOperationHandle<CardAssetDataSO> Handle { get; }
        public CardAssetDataSO Asset { get; }
        public int RefCount { get; set; }
    }
}
