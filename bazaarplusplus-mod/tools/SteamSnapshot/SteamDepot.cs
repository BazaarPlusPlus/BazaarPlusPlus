using System.Security.Cryptography;
using SteamKit2;
using SteamKit2.CDN;

namespace BazaarPlusPlus.SteamSnapshot;

/// <summary>Reads a branch and downloads the snapshot files of one of its depots.</summary>
public static class SteamDepot
{
    private const string PublicBranch = "public";
    private const int MaxChunkBytes = 16 * 1024 * 1024;
    private const int ChunkAttempts = 5;

    public static async Task<BranchState> ReadBranch(
        SteamSession session,
        uint app,
        string branch,
        IEnumerable<uint> depots
    )
    {
        var tokens = await session.Apps.PICSGetAccessTokens([app], []);
        tokens.AppTokens.TryGetValue(app, out var token);
        if (branch != PublicBranch)
            return BranchState.Read(
                await PrivateDepotSection(session, app, token, branch),
                branch,
                depots
            );
        var info = await session.Apps.PICSGetProductInfo(
            [new SteamApps.PICSRequest(app, token)],
            []
        );
        var product = info.Results!.SelectMany(result => result.Apps)
            .First(pair => pair.Key == app);
        return BranchState.Read(product.Value.KeyValues["depots"], branch, depots);
    }

    // Steam leaves a password-protected branch out of the app's product info. Its access code
    // gives a key, and the key gives the depots section of that branch.
    private static async Task<KeyValue> PrivateDepotSection(
        SteamSession session,
        uint app,
        ulong token,
        string branch
    )
    {
        var code = BetaPasswords
            .Parse(Environment.GetEnvironmentVariable(BetaPasswords.EnvironmentVariable))
            .For(branch);
        var check = await session.Apps.CheckAppBetaPassword(app, code);
        if (!check.BetaPasswords.TryGetValue(branch, out var key))
            throw new InvalidOperationException(
                $"The access code of branch {branch} unlocked nothing for it ({check.Result}); the code or the branch may have changed"
            );
        var beta = await session.Apps.PICSGetPrivateBeta(app, token, branch, key);
        if (beta.Result != EResult.OK)
            throw new InvalidOperationException(
                $"Steam refused the depots of branch {branch}: {beta.Result}"
            );
        return beta.DepotSection;
    }

    /// <summary>Writes the snapshot files under <paramref name="output"/>; returns the branch state read.</summary>
    public static async Task<BranchState> Download(
        SteamSession session,
        uint app,
        string branch,
        uint depot,
        string output
    )
    {
        var state = await ReadBranch(session, app, branch, [depot]);
        var manifestId = state.Manifests[depot];
        var key = await session.Apps.GetDepotDecryptionKey(depot, app);
        if (key.Result != EResult.OK)
            throw new InvalidOperationException(
                $"The account cannot read depot {depot}: {key.Result}"
            );
        var code = await session.Content.GetManifestRequestCode(depot, app, manifestId, branch);
        if (code == 0)
            throw new InvalidOperationException(
                $"Steam refused the manifest of depot {depot} on branch {branch}"
            );
        var servers = await ContentServers(session, app);
        using var cdn = new Client(session.Client);
        var manifest = await WithServers(
            servers,
            server => cdn.DownloadManifestAsync(depot, manifestId, code, server, key.DepotKey)
        );
        if (manifest.DepotID != depot || manifest.FilenamesEncrypted || manifest.Files is null)
            throw new InvalidDataException($"Steam sent an invalid manifest for depot {depot}");

        var files = manifest
            .Files.Where(file => (file.Flags & EDepotFileFlag.Directory) == 0)
            .ToDictionary(file => file.FileName);
        var selected = SnapshotFiles.Select(files.Keys);
        await Parallel.ForEachAsync(
            selected,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (file, cancellation) =>
                await DownloadFile(
                    cdn,
                    servers,
                    depot,
                    key.DepotKey,
                    files[file.DepotName],
                    Path.Combine(output, file.Target),
                    cancellation
                )
        );
        return state;
    }

    private static async Task DownloadFile(
        Client cdn,
        IReadOnlyList<Server> servers,
        uint depot,
        byte[] depotKey,
        DepotManifest.FileData file,
        string target,
        CancellationToken cancellation
    )
    {
        if (!string.IsNullOrEmpty(file.LinkTarget))
            throw new InvalidDataException($"The snapshot file {file.FileName} is a link");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var partial = $"{target}.partial";
        await using (var stream = File.Create(partial))
        {
            stream.SetLength(checked((long)file.TotalSize));
            foreach (var chunk in file.Chunks.OrderBy(chunk => chunk.Offset))
            {
                if (
                    chunk.UncompressedLength > MaxChunkBytes
                    || chunk.Offset + chunk.UncompressedLength > file.TotalSize
                )
                    throw new InvalidDataException($"{file.FileName} has an invalid chunk");
                var bytes = new byte[chunk.UncompressedLength];
                var written = await WithServers(
                    servers,
                    server => cdn.DownloadDepotChunkAsync(depot, chunk, server, bytes, depotKey)
                );
                stream.Position = checked((long)chunk.Offset);
                await stream.WriteAsync(bytes.AsMemory(0, written), cancellation);
            }
            stream.Position = 0;
            var hash = await SHA1.HashDataAsync(stream, cancellation);
            if (file.TotalSize > 0 && !hash.AsSpan().SequenceEqual(file.FileHash))
                throw new InvalidDataException($"{file.FileName} does not match its manifest hash");
        }
        File.Move(partial, target, overwrite: true);
    }

    private static async Task<IReadOnlyList<Server>> ContentServers(SteamSession session, uint app)
    {
        var servers = (await session.Content.GetServersForSteamPipe())
            .Where(server =>
                !server.UseAsProxy
                && !server.SteamChinaOnly
                && server.Protocol == Server.ConnectionProtocol.HTTPS
                && (server.AllowedAppIds.Length == 0 || server.AllowedAppIds.Contains(app))
            )
            .OrderBy(server => server.WeightedLoad)
            .Take(8)
            .ToList();
        if (servers.Count == 0)
            throw new InvalidOperationException("Steam gave no HTTPS content server");
        return servers;
    }

    private static async Task<T> WithServers<T>(
        IReadOnlyList<Server> servers,
        Func<Server, Task<T>> request
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await request(servers[(attempt - 1) % servers.Count]);
            }
            catch (Exception error)
                when (attempt < ChunkAttempts
                    && error
                        is HttpRequestException
                            or SteamKitWebRequestException
                            or IOException
                            or TaskCanceledException
                )
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt));
            }
        }
    }
}
