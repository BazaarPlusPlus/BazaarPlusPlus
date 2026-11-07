#nullable enable
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Infrastructure.HeroPortraits.Composition;

internal sealed record PortraitResourceEntry(
    string Source,
    string NumericKey,
    PortraitComposition Fit
);

// This index contains references to the existing numeric cache, never game artwork.
// Load once off-thread; setup hooks only consult the resulting in-memory dictionary.
internal sealed class PortraitResourceCache
{
    private sealed record Link(int Version, int Algorithm, string Source, string NumericKey);

    private const int Version = 1;
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, PortraitResourceEntry> _ready = new(
        StringComparer.Ordinal
    );
    private readonly ConcurrentDictionary<string, byte> _unmapped = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _misses = new(StringComparer.Ordinal);
    private readonly Task _loading;
    private bool _legacyWindow;
    private int _legacyProbes;

    internal PortraitResourceCache(string directory)
    {
        _directory = directory;
        _loading = Task.Run(Load);
    }

    internal Task Loading => _loading;

    internal bool TryGet(string key, out PortraitResourceEntry entry) =>
        _ready.TryGetValue(key, out entry!);

    internal bool CanProbeLegacy(string key) =>
        _loading.IsCompleted
        && _legacyWindow
        && !_unmapped.IsEmpty
        && !_misses.ContainsKey(key)
        && Interlocked.Increment(ref _legacyProbes) <= 64;

    internal void MarkMiss(string key)
    {
        _misses.TryAdd(key, 0);
        _ready.TryRemove(key, out _);
    }

    internal void Record(string key, string source, string numericKey, PortraitComposition fit)
    {
        if (!ValidKey(key) || !ValidKey(numericKey) || !PortraitCompositionMatcher.IsReliable(fit))
            return;
        _ready[key] = new PortraitResourceEntry(source, numericKey, fit);
        _unmapped.TryRemove(numericKey, out _);
        _misses.TryRemove(key, out _);
        if (!File.Exists(Path.Combine(_directory, numericKey + ".json")))
            return;
        try
        {
            AtomicFileWriter.Write(
                Path.Combine(_directory, "Resources", key + ".json"),
                temp =>
                    File.WriteAllText(
                        temp,
                        JsonConvert.SerializeObject(
                            new Link(
                                Version,
                                PortraitCompositionCache.AlgorithmVersion,
                                source,
                                numericKey
                            )
                        )
                    )
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Load()
    {
        try
        {
            if (!Directory.Exists(_directory))
                return;
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Take(2048))
            {
                var key = Path.GetFileNameWithoutExtension(path);
                if (ValidKey(key))
                    _unmapped.TryAdd(key, 0);
            }
            var links = Path.Combine(_directory, "Resources");
            foreach (
                var path in Directory.Exists(links)
                    ? Directory.EnumerateFiles(links, "*.json").Take(1024)
                    : []
            )
            {
                try
                {
                    var key = Path.GetFileNameWithoutExtension(path);
                    if (!ValidKey(key) || new FileInfo(path).Length > 4096)
                        continue;
                    var link = JsonConvert.DeserializeObject<Link>(File.ReadAllText(path));
                    if (
                        link == null
                        || link.Version != Version
                        || link.Algorithm != PortraitCompositionCache.AlgorithmVersion
                        || !ValidKey(link.NumericKey)
                        || string.IsNullOrEmpty(link.Source)
                    )
                        continue;
                    var numeric = Path.Combine(_directory, link.NumericKey + ".json");
                    if (!File.Exists(numeric) || new FileInfo(numeric).Length > 4096)
                        continue;
                    var fit = JsonConvert.DeserializeObject<PortraitComposition>(
                        File.ReadAllText(numeric)
                    );
                    if (fit == null || !PortraitCompositionMatcher.IsReliable(fit))
                        continue;
                    // A new live result wins if loading overlapped the first encounter.
                    _ready.TryAdd(
                        key,
                        new PortraitResourceEntry(link.Source, link.NumericKey, fit)
                    );
                    _unmapped.TryRemove(link.NumericKey, out _);
                }
                catch (Exception ex)
                    when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
            // Only the first upgraded session probes anonymous old entries. Retained v2
            // aliases or never-used skins must not delay every future unknown portrait.
            var marker = Path.Combine(links, "migration-v1.done");
            if (!_unmapped.IsEmpty && !File.Exists(marker))
            {
                AtomicFileWriter.Write(marker, temp => File.WriteAllText(temp, "1"));
                _legacyWindow = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool ValidKey(string value) =>
        value != null
        && value.Length == 64
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
