#nullable enable
using System.Text;

namespace BazaarPlusPlus.RemoteEmbeddedDataFetcher;

internal readonly record struct RemoteEmbeddedDataRequest(
    Uri Source,
    string DestinationPath,
    long MinimumBytes
);

internal readonly record struct SeedPromotion(string StagedPath, string CanonicalPath);

internal static class RemoteEmbeddedDataFetch
{
    internal static async Task FetchAsync(
        HttpClient client,
        RemoteEmbeddedDataRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        if (request.MinimumBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
            throw new ArgumentException("A destination path is required.", nameof(request));

        var directory = Path.GetDirectoryName(request.DestinationPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The destination must have a directory.", nameof(request));
        Directory.CreateDirectory(directory);
        var temporaryPath = request.DestinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await client
                .GetAsync(
                    request.Source,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                )
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (
                var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true
                )
            )
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            ValidateTransport(temporaryPath, request.MinimumBytes);
            File.Move(temporaryPath, request.DestinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static void PromoteSeedSet(IReadOnlyList<SeedPromotion> seeds) =>
        PromoteSeedSet(seeds, beforePromote: null);

    internal static void PromoteSeedSet(
        IReadOnlyList<SeedPromotion> seeds,
        Action<int>? beforePromote
    )
    {
        ArgumentNullException.ThrowIfNull(seeds);
        if (seeds.Count == 0)
            throw new ArgumentException("At least one seed is required.", nameof(seeds));
        foreach (var seed in seeds)
        {
            if (!File.Exists(seed.StagedPath))
                throw new FileNotFoundException("A staged seed is missing.", seed.StagedPath);
        }

        var transactionRoot = Path.Combine(
            Path.GetTempPath(),
            "bpp-seed-promote-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(transactionRoot);
        var backups = new string?[seeds.Count];
        try
        {
            for (var i = 0; i < seeds.Count; i++)
            {
                var canonical = seeds[i].CanonicalPath;
                var directory = Path.GetDirectoryName(canonical);
                if (string.IsNullOrWhiteSpace(directory))
                    throw new ArgumentException("A canonical seed must have a directory.");
                Directory.CreateDirectory(directory);
                if (!File.Exists(canonical))
                    continue;
                backups[i] = Path.Combine(transactionRoot, i + ".backup");
                File.Copy(canonical, backups[i]!);
            }

            try
            {
                for (var i = 0; i < seeds.Count; i++)
                {
                    beforePromote?.Invoke(i);
                    ReplaceFromCopy(seeds[i].StagedPath, seeds[i].CanonicalPath);
                }
            }
            catch
            {
                for (var i = 0; i < seeds.Count; i++)
                {
                    if (backups[i] != null)
                        ReplaceFromCopy(backups[i]!, seeds[i].CanonicalPath);
                    else if (File.Exists(seeds[i].CanonicalPath))
                        File.Delete(seeds[i].CanonicalPath);
                }
                throw;
            }
        }
        finally
        {
            Directory.Delete(transactionRoot, recursive: true);
        }
    }

    private static void ValidateTransport(string path, long minimumBytes)
    {
        var length = new FileInfo(path).Length;
        if (length < minimumBytes)
        {
            throw new InvalidDataException(
                $"downloaded {length} bytes, below the required minimum of {minimumBytes} bytes"
            );
        }

        using var reader = new StreamReader(
            path,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        int next;
        do
        {
            next = reader.Read();
        } while (next >= 0 && char.IsWhiteSpace((char)next));
        if (next != '{' && next != '[')
        {
            throw new InvalidDataException(
                "the first non-whitespace character is neither '{' nor '['"
            );
        }
    }

    private static void ReplaceFromCopy(string source, string destination)
    {
        var temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporaryPath);
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
