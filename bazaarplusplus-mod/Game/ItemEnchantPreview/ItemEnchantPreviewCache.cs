using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewCache
{
    private sealed class CacheEntry
    {
        public DateTime CachedAtUtc;
        public List<TooltipSegment> Segments = new List<TooltipSegment>();
    }

    private static readonly Dictionary<string, CacheEntry> Cache =
        new Dictionary<string, CacheEntry>();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public static string CreateKey(ItemEnchantPreviewSnapshot snapshot)
    {
        var attributes = string.Join(
            ",",
            snapshot
                .PreviewAttributes.OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value}")
        );

        return $"{snapshot.InstanceId}|{snapshot.TemplateId}|{snapshot.Section}|{snapshot.CurrentEnchantment}|{snapshot.PreviewEnchantment}|{attributes}";
    }

    public static bool TryGet(
        ItemEnchantPreviewSnapshot snapshot,
        out List<TooltipSegment> segments
    )
    {
        var key = CreateKey(snapshot);

        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.CachedAtUtc <= CacheDuration)
                {
                    segments = new List<TooltipSegment>(entry.Segments);
                    return true;
                }

                Cache.Remove(key);
            }
        }

        segments = null;
        return false;
    }

    public static void Save(ItemEnchantPreviewSnapshot snapshot, List<TooltipSegment> segments)
    {
        var key = CreateKey(snapshot);

        lock (Cache)
        {
            Cache[key] = new CacheEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Segments = new List<TooltipSegment>(segments),
            };

            if (Cache.Count > 256)
                Cache.Clear();
        }
    }
}
