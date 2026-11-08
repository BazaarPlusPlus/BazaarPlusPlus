using BazaarPlusPlus.HistorySampler;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.Sqlite;

const string Usage = """
    History Sampler: copy recent cloud runs into a local history database as the local player's
    own History-Only Runs, so History can be checked without playing. Development only.

      import --data-root DIR [--journal wal|delete] [--count N] [--hours H]
             [--bundle FILE]... [--account ID] [--api URL] [--changes FILE]
      clear  --data-root DIR [--journal wal|delete] [--changes FILE]

    import  samples --count runs (default 5) from the Bundles of the last --hours (default 24,
            at most 191), keeping runs with at least one battle whose four card sets were
            captured. --bundle imports local Bundle files (.bundle bytes or .b64) instead.
    clear   removes every sampled run with its battles, favorites, videos and replay files.

    DIR is a Data Root (the folder holding bazaarplusplus.db); the game must not be running on
    it. --journal is the mod's journal mode for that Data Root: wal on the desktop, delete on
    Android. The cloud feed is the mod's own API (--api overrides it) and needs the bundle_sync
    token in BPP_BUNDLE_SYNC_TOKEN (the root's `just history-sample` supplies it). The account
    is the newest local run's unless --account names one. --changes writes one "put PATH" or
    "rm PATH" line per file written or deleted, relative to DIR, for a caller that mirrors the
    Data Root.
    """;

try
{
    return await Run(args);
}
catch (Exception error)
    when (error is InvalidOperationException or ArgumentException or IOException)
{
    Console.Error.WriteLine($"history-sample: {error.Message}");
    return 1;
}

static async Task<int> Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine(Usage);
        return args.Length == 0 ? 2 : 0;
    }
    var command = args[0];
    var options = Options.Parse(args.Skip(1).ToArray());
    var store = new SampleStore(options.DataRoot, options.Journal);
    SampleResult result;
    switch (command)
    {
        case "import":
            result = await Import(store, options);
            Console.WriteLine(
                $"Imported {result.RunIds.Count} run(s), {result.Battles} battle(s), {result.Replays} replay(s) into {options.DataRoot}."
            );
            foreach (var runId in result.RunIds)
                Console.WriteLine($"  {runId}");
            break;
        case "clear":
            result = store.Clear();
            Console.WriteLine(
                $"Removed {result.RunIds.Count} sampled run(s) and {result.Battles} battle(s) from {options.DataRoot}."
            );
            break;
        default:
            throw new ArgumentException($"Unknown command '{command}'.\n\n{Usage}");
    }
    if (options.Changes != null)
        await File.WriteAllLinesAsync(
            options.Changes,
            result.Files.Select(change =>
                $"{(change.Written ? "put" : "rm")} {change.RelativePath.Replace('\\', '/')}"
            )
        );
    return 0;
}

static async Task<SampleResult> Import(SampleStore store, Options options)
{
    var player = store.ResolveLocalPlayer(options.Account);
    var result = new SampleResult();
    if (options.Bundles.Count > 0)
    {
        foreach (var file in options.Bundles)
        {
            var opened = OpenBundle(ReadBundleFile(file), file);
            if (opened == null || !store.Import(opened, player, result))
                Console.Error.WriteLine($"history-sample: skipped {file}.");
        }
        return result;
    }

    using var feed = new BundleFeed(
        options.Api ?? BazaarPlusPlus.ModApi.ModApiUploadDefaults.ApiBaseUrl,
        SyncToken()
    );
    var items = await feed.ListAsync(options.Hours, CancellationToken.None);
    if (items.Count == 0)
        throw new InvalidOperationException(
            $"The server listed no Bundle in the last {options.Hours} hour(s)."
        );
    var tries = 0;
    foreach (var item in items.OrderBy(_ => Random.Shared.Next()))
    {
        if (result.RunIds.Count >= options.Count || tries++ >= options.Count * 8)
            break;
        byte[] bytes;
        try
        {
            bytes = await feed.DownloadAsync(item, CancellationToken.None);
        }
        catch (Exception error)
            when (error
                    is HttpRequestException
                        or InvalidOperationException
                        or TaskCanceledException
            )
        {
            Console.Error.WriteLine($"history-sample: {error.Message}");
            continue;
        }
        var opened = OpenBundle(bytes, item.BundleId);
        if (opened != null && HasBoards(opened))
            store.Import(opened, player, result);
    }
    return result;
}

static OpenedRunBundleV6? OpenBundle(byte[] bytes, string label)
{
    var opened = RunBundleV6Contract.Open(bytes);
    if (opened.Succeeded)
        return opened.Value;
    Console.Error.WriteLine($"history-sample: {label} does not open: {opened.Reason}");
    return null;
}

// A run worth looking at in History shows at least one battle with both boards.
static bool HasBoards(OpenedRunBundleV6 bundle) =>
    bundle.Payload.Battles?.Any(battle =>
        battle?.Snapshots?.CardSets is { Count: 4 } sets
        && sets.All(set =>
            !string.Equals(set.Status, "Missing", StringComparison.OrdinalIgnoreCase)
        )
    ) == true;

static byte[] ReadBundleFile(string path) =>
    path.EndsWith(".b64", StringComparison.OrdinalIgnoreCase)
        ? Convert.FromBase64String(File.ReadAllText(path).Trim())
        : File.ReadAllBytes(path);

static string SyncToken() =>
    Environment.GetEnvironmentVariable("BPP_BUNDLE_SYNC_TOKEN") is { Length: > 0 } token
        ? token
        : throw new InvalidOperationException(
            "BPP_BUNDLE_SYNC_TOKEN is not set; run the root's `just history-sample`, or pass --bundle FILE."
        );

internal sealed class Options
{
    public string DataRoot { get; private set; } = string.Empty;
    public SqliteOpenOptions Journal { get; private set; } = SqliteOpenOptions.Default;
    public int Count { get; private set; } = 5;
    public int Hours { get; private set; } = 24;
    public List<string> Bundles { get; } = new();
    public string? Account { get; private set; }
    public string? Changes { get; private set; }
    public string? Api { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() =>
                i + 1 < args.Length
                    ? args[++i]
                    : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--data-root":
                    options.DataRoot = Next();
                    break;
                case "--journal":
                    options.Journal = Next() switch
                    {
                        "wal" => SqliteOpenOptions.Default,
                        "delete" => new SqliteOpenOptions(SqliteJournalMode.Delete),
                        var other => throw new ArgumentException(
                            $"--journal is wal or delete, not '{other}'."
                        ),
                    };
                    break;
                case "--count":
                    options.Count = PositiveInt(Next(), "--count");
                    break;
                case "--hours":
                    options.Hours = Math.Min(
                        PositiveInt(Next(), "--hours"),
                        BundleFeed.MaxLookbackHours
                    );
                    break;
                case "--bundle":
                    options.Bundles.Add(Next());
                    break;
                case "--account":
                    options.Account = Next();
                    break;
                case "--api":
                    options.Api = Next();
                    break;
                case "--changes":
                    options.Changes = Next();
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }
        if (options.DataRoot.Length == 0)
            throw new ArgumentException("--data-root is required.");
        return options;
    }

    private static int PositiveInt(string value, string name) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{name} must be a positive whole number.");
}
