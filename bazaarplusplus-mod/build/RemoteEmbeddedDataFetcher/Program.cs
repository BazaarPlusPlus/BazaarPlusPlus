using System.Net.Http.Headers;
using BazaarPlusPlus.RemoteEmbeddedDataFetcher;

try
{
    return args.Length == 0
        ? Usage()
        : args[0] switch
        {
            "fetch" => await FetchAsync(args),
            "promote" => Promote(args),
            _ => Usage(),
        };
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"[BazaarPlusPlus] Remote embedded data operation failed: {ex.Message}"
    );
    return 2;
}

static async Task<int> FetchAsync(string[] args)
{
    if (args.Length != 5 || !long.TryParse(args[3], out var minimumBytes))
        return Usage();

    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    client.DefaultRequestHeaders.UserAgent.Add(
        new ProductInfoHeaderValue("BazaarPlusPlusBuild", args[4])
    );
    Console.WriteLine($"[BazaarPlusPlus] Downloading {args[1]} to {args[2]}");
    await RemoteEmbeddedDataFetch.FetchAsync(
        client,
        new RemoteEmbeddedDataRequest(new Uri(args[1]), args[2], minimumBytes)
    );
    return 0;
}

static int Promote(string[] args)
{
    if (args.Length < 4)
        return Usage();
    var stagedDirectory = args[1];
    var canonicalDirectory = args[2];
    var seeds = args.Skip(3)
        .Select(name => new SeedPromotion(
            Path.Combine(stagedDirectory, name),
            Path.Combine(canonicalDirectory, name)
        ))
        .ToArray();
    RemoteEmbeddedDataFetch.PromoteSeedSet(seeds);
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine(
        "Usage: RemoteEmbeddedDataFetcher fetch <url> <destination> <min-bytes> <version>"
    );
    Console.Error.WriteLine(
        "       RemoteEmbeddedDataFetcher promote <staged-directory> <canonical-directory> <file>..."
    );
    return 1;
}
