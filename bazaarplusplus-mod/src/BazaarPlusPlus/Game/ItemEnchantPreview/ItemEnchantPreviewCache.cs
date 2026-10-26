#nullable enable
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

    /// <summary>
    /// Appends the cached segments for <paramref name="key"/> (from <see cref="CreateKey"/>) to
    /// <paramref name="destination"/> while the entry is younger than the TTL.
    /// </summary>
    public static bool TryAppend(string key, List<TooltipSegment> destination)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var entry))
                return false;
            if (DateTime.UtcNow - entry.CachedAtUtc <= CacheDuration)
            {
                destination.AddRange(entry.Segments);
                return true;
            }

            Cache.Remove(key);
            return false;
        }
    }

    public static void Save(string key, List<TooltipSegment> segments)
    {
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
