using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using BazaarPlusPlus.RemoteEmbeddedDataFetcher;
using BazaarPlusPlus.TestSupport;

await TestFetchValidatesStatusLengthAndJsonAndCleansTemporaryFiles();
await TestFetchRetriesTransientTransportFailure();
await TestConnectionsRotateAcrossResolvedAddresses();
await TestFetchRetriesTransientHttpStatus();
await TestFetchStopsAfterBoundedTransientRetries();
await TestFetchDoesNotRetryPermanentHttpStatus();
TestSeedSetPromotionIsAtomicAcrossFiles();
await TestMsBuildTargetUsesFetcherHonorsExistingFilesAndPropagatesExitCode();
await TestFetchDataRejectsBadSchemaWithoutChangingCanonicalSet();

Console.WriteLine("Remote embedded data pipeline checks passed.");

static async Task TestFetchValidatesStatusLengthAndJsonAndCleansTemporaryFiles()
{
    var root = TemporaryDirectory();
    try
    {
        var destination = Path.Combine(root, "seed.json");
        File.WriteAllText(destination, "[\"old\"]");
        using (var client = Client(HttpStatusCode.OK, " [\"new\"] "))
        {
            await RemoteEmbeddedDataFetch.FetchAsync(
                client,
                new RemoteEmbeddedDataRequest(new Uri("https://example.test/seed"), destination, 5)
            );
        }
        Equal(
            " [\"new\"] ",
            TestInputs.Scratch(destination),
            "A valid fetch should replace the seed."
        );

        await MustFailFetch(destination, HttpStatusCode.ServiceUnavailable, "[\"down\"]", 1);
        await MustFailFetch(destination, HttpStatusCode.OK, "[]", 100);
        await MustFailFetch(destination, HttpStatusCode.OK, "not-json", 1);
        Equal(
            " [\"new\"] ",
            TestInputs.Scratch(destination),
            "Failed transport validation must preserve the canonical bytes."
        );
        False(
            Directory
                .EnumerateFiles(root)
                .Any(path => path.EndsWith(".tmp", StringComparison.Ordinal)),
            "Every failed fetch must clean its temporary file."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestFetchRetriesTransientTransportFailure()
{
    var root = TemporaryDirectory();
    try
    {
        var destination = Path.Combine(root, "seed.json");
        using var handler = new TransientThenSuccessHandler("[\"recovered\"]");
        using var client = new HttpClient(handler);

        await RemoteEmbeddedDataFetch.FetchAsync(
            client,
            new RemoteEmbeddedDataRequest(new Uri("https://example.test/seed"), destination, 5)
        );

        Equal(2, handler.RequestCount, "A transient transport failure should be retried once.");
        Equal(
            "[\"recovered\"]",
            TestInputs.Scratch(destination),
            "The successful retry should publish the downloaded seed."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestConnectionsRotateAcrossResolvedAddresses()
{
    var firstAddress = IPAddress.Parse("192.0.2.10");
    var secondAddress = IPAddress.Parse("192.0.2.20");
    var connectedAddresses = new List<IPAddress>();
    var resolutionCount = 0;
    var connector = new RotatingDnsConnector(
        (_, _) =>
        {
            resolutionCount++;
            return Task.FromResult(
                resolutionCount == 1
                    ? new[] { firstAddress, secondAddress }
                    : new[] { secondAddress, firstAddress }
            );
        },
        (address, _, _) =>
        {
            connectedAddresses.Add(address);
            return ValueTask.FromResult<Stream>(new MemoryStream());
        }
    );
    var endpoint = new DnsEndPoint("example.test", 443);

    await using (await connector.ConnectAsync(endpoint, CancellationToken.None)) { }
    await using (await connector.ConnectAsync(endpoint, CancellationToken.None)) { }

    Equal(2, connectedAddresses.Count, "Two connections should be attempted.");
    Equal(1, resolutionCount, "An address set should stay stable for one fetch lifecycle.");
    Equal(
        firstAddress,
        connectedAddresses[0],
        "The first connection should use the first address."
    );
    Equal(
        secondAddress,
        connectedAddresses[1],
        "A fresh connection should rotate to the next resolved address."
    );
}

static async Task TestFetchRetriesTransientHttpStatus()
{
    var root = TemporaryDirectory();
    try
    {
        var destination = Path.Combine(root, "seed.json");
        using var handler = new TransientStatusThenSuccessHandler("[\"recovered\"]");
        using var client = new HttpClient(handler);

        await RemoteEmbeddedDataFetch.FetchAsync(
            client,
            new RemoteEmbeddedDataRequest(new Uri("https://example.test/seed"), destination, 5)
        );

        Equal(2, handler.RequestCount, "A service-unavailable response should be retried once.");
        Equal(
            "[\"recovered\"]",
            TestInputs.Scratch(destination),
            "The successful status retry should publish the downloaded seed."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestFetchStopsAfterBoundedTransientRetries()
{
    var root = TemporaryDirectory();
    try
    {
        var destination = Path.Combine(root, "seed.json");
        File.WriteAllText(destination, "[\"old\"]");
        using var handler = new AlwaysTransientHandler();
        using var client = new HttpClient(handler);

        try
        {
            await RemoteEmbeddedDataFetch.FetchAsync(
                client,
                new RemoteEmbeddedDataRequest(new Uri("https://example.test/seed"), destination, 5)
            );
            throw new InvalidOperationException("Exhausted transient retries were ignored.");
        }
        catch (HttpRequestException) { }

        Equal(5, handler.RequestCount, "Transient retries must stop after five attempts.");
        Equal(
            "[\"old\"]",
            TestInputs.Scratch(destination),
            "Exhausted retries must preserve the canonical seed."
        );
        False(
            Directory
                .EnumerateFiles(root)
                .Any(path => path.EndsWith(".tmp", StringComparison.Ordinal)),
            "Exhausted retries must clean every temporary file."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestFetchDoesNotRetryPermanentHttpStatus()
{
    var root = TemporaryDirectory();
    try
    {
        var destination = Path.Combine(root, "seed.json");
        using var handler = new CountingStatusHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);

        try
        {
            await RemoteEmbeddedDataFetch.FetchAsync(
                client,
                new RemoteEmbeddedDataRequest(new Uri("https://example.test/seed"), destination, 5)
            );
            throw new InvalidOperationException("A permanent HTTP failure was ignored.");
        }
        catch (HttpRequestException) { }

        Equal(1, handler.RequestCount, "A permanent HTTP status must fail without retrying.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void TestSeedSetPromotionIsAtomicAcrossFiles()
{
    var root = TemporaryDirectory();
    try
    {
        var stage = Path.Combine(root, "stage");
        var canonical = Path.Combine(root, "canonical");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(canonical);
        var seeds = new[]
        {
            Promotion(stage, canonical, "voice.json", "new-voice", "old-voice"),
            Promotion(stage, canonical, "builds.json", "new-builds", "old-builds"),
        };

        try
        {
            RemoteEmbeddedDataFetch.PromoteSeedSet(
                seeds,
                index =>
                {
                    if (index == 1)
                        throw new IOException("second promotion failed");
                }
            );
            throw new InvalidOperationException(
                "The injected second promotion failure was ignored."
            );
        }
        catch (IOException) { }

        Equal(
            "old-voice",
            TestInputs.Scratch(seeds[0].CanonicalPath),
            "Rollback must restore file one."
        );
        Equal(
            "old-builds",
            TestInputs.Scratch(seeds[1].CanonicalPath),
            "Rollback must restore file two."
        );

        RemoteEmbeddedDataFetch.PromoteSeedSet(seeds);
        Equal(
            "new-voice",
            TestInputs.Scratch(seeds[0].CanonicalPath),
            "Promotion should replace file one."
        );
        Equal(
            "new-builds",
            TestInputs.Scratch(seeds[1].CanonicalPath),
            "Promotion should replace file two."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestMsBuildTargetUsesFetcherHonorsExistingFilesAndPropagatesExitCode()
{
    var repo = RepoRoot();
    var root = TemporaryDirectory();
    try
    {
        var destination = Path.Combine(root, "data");
        var server = new LoopbackServer(
            new string(' ', 102398).Insert(0, "[") + "]",
            new string(' ', 51198).Insert(0, "[") + "]"
        );
        await using (server)
        {
            var project = WriteMsBuildProject(repo, root, destination, server.BaseUrl);
            var first = await RunDotnet(
                repo,
                "msbuild",
                project,
                "-t:FetchRemoteEmbeddedData",
                "-p:ForceRemoteEmbeddedDataRefresh=true"
            );
            Equal(0, first.ExitCode, "The loopback force fetch should succeed. " + first.Output);
            await server.Completion;
            Equal(2, server.Requests.Count, "Force refresh should fetch both declared resources.");
            True(
                server.Requests.All(request =>
                    request.Contains(
                        "User-Agent: BazaarPlusPlusBuild/4.6.0",
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
                "Build fetches should carry the standard build user agent."
            );

            var skipped = await RunDotnet(
                repo,
                "msbuild",
                project,
                "-t:FetchRemoteEmbeddedData",
                "-p:VoiceLinesRemoteUrl=http://127.0.0.1:1/unreachable",
                "-p:TenWinBuildsRemoteUrl=http://127.0.0.1:1/unreachable",
                "-p:ForceRemoteEmbeddedDataRefresh=false"
            );
            Equal(0, skipped.ExitCode, "Existing non-force seeds should not access the network.");
        }

        var failureServer = new LoopbackServer(
            Enumerable.Repeat("failure", 5),
            HttpStatusCode.BadGateway
        );
        await using (failureServer)
        {
            var failedDestination = Path.Combine(root, "failed-data");
            var project = WriteMsBuildProject(
                repo,
                Path.Combine(root, "failed"),
                failedDestination,
                failureServer.BaseUrl
            );
            var failed = await RunDotnet(
                repo,
                "msbuild",
                project,
                "-t:FetchRemoteEmbeddedData",
                "-p:ForceRemoteEmbeddedDataRefresh=true"
            );
            True(failed.ExitCode != 0, "A fetcher failure must propagate through MSBuild.");
        }
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestFetchDataRejectsBadSchemaWithoutChangingCanonicalSet()
{
    var repo = RepoRoot();
    var root = TemporaryDirectory();
    try
    {
        var canonical = Path.Combine(root, "canonical");
        Directory.CreateDirectory(canonical);
        var voicePath = Path.Combine(canonical, "voice-lines.json");
        var buildsPath = Path.Combine(canonical, "builds.json");
        File.WriteAllText(voicePath, "old-voice");
        File.WriteAllText(buildsPath, "old-builds");
        var invalidVoice = "[" + new string(' ', 102398) + "]";
        var invalidBuilds = "[" + new string(' ', 51198) + "]";
        var server = new LoopbackServer(invalidVoice, invalidBuilds);
        await using (server)
        {
            var result = await RunProcess(
                repo,
                "bash",
                Path.Combine(repo, "scripts", "build.sh"),
                "fetch-data",
                $"-p:VoiceLinesRemoteUrl={server.BaseUrl}voice",
                $"-p:TenWinBuildsRemoteUrl={server.BaseUrl}builds",
                $"-p:RemoteEmbeddedDataDirectory={canonical}"
            );
            True(result.ExitCode != 0, "A real feature parser must reject a bad staged schema.");
            await server.Completion;
        }

        Equal(
            "old-voice",
            TestInputs.Scratch(voicePath),
            "A failed seed gate must preserve voice bytes."
        );
        Equal(
            "old-builds",
            TestInputs.Scratch(buildsPath),
            "A failed seed gate must preserve build bytes."
        );
        False(
            Directory
                .EnumerateDirectories(root)
                .Any(path =>
                    Path.GetFileName(path)
                        .StartsWith(".remote-data-stage.", StringComparison.Ordinal)
                ),
            "A failed transactional fetch must clean its staging directory."
        );
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task MustFailFetch(
    string destination,
    HttpStatusCode status,
    string content,
    long minimumBytes
)
{
    using var client = Client(status, content);
    try
    {
        await RemoteEmbeddedDataFetch.FetchAsync(
            client,
            new RemoteEmbeddedDataRequest(
                new Uri("https://example.test/seed"),
                destination,
                minimumBytes
            )
        );
        throw new InvalidOperationException("An invalid fetch unexpectedly succeeded.");
    }
    catch (Exception ex) when (ex is HttpRequestException or InvalidDataException) { }
}

static HttpClient Client(HttpStatusCode status, string content) =>
    new(new StaticHandler(status, content));

static SeedPromotion Promotion(
    string stage,
    string canonical,
    string name,
    string stagedContent,
    string canonicalContent
)
{
    var stagedPath = Path.Combine(stage, name);
    var canonicalPath = Path.Combine(canonical, name);
    File.WriteAllText(stagedPath, stagedContent);
    File.WriteAllText(canonicalPath, canonicalContent);
    return new SeedPromotion(stagedPath, canonicalPath);
}

static string WriteMsBuildProject(
    string repo,
    string projectDirectory,
    string destination,
    string baseUrl
)
{
    Directory.CreateDirectory(projectDirectory);
    var path = Path.Combine(projectDirectory, "Fetch.proj");
    var targets = Path.Combine(repo, "src", "BazaarPlusPlus", "RemoteEmbeddedData.targets");
    var fetcher = Path.Combine(
        repo,
        "build",
        "RemoteEmbeddedDataFetcher",
        "RemoteEmbeddedDataFetcher.csproj"
    );
    File.WriteAllText(
        path,
        $"""
        <Project>
          <PropertyGroup>
            <BppVersion>4.6.0</BppVersion>
            <VoiceLinesRemoteUrl>{SecurityElement.Escape(baseUrl + "voice")}</VoiceLinesRemoteUrl>
            <TenWinBuildsRemoteUrl>{SecurityElement.Escape(
            baseUrl + "builds"
        )}</TenWinBuildsRemoteUrl>
            <RemoteEmbeddedDataDirectory>{SecurityElement.Escape(
            destination
        )}</RemoteEmbeddedDataDirectory>
            <RemoteEmbeddedDataFetcherProject>{SecurityElement.Escape(
            fetcher
        )}</RemoteEmbeddedDataFetcherProject>
          </PropertyGroup>
          <Import Project="{SecurityElement.Escape(targets)}" />
        </Project>
        """
    );
    return path;
}

static async Task<(int ExitCode, string Output)> RunDotnet(
    string workingDirectory,
    params string[] arguments
) => await RunProcess(workingDirectory, "dotnet", arguments);

static async Task<(int ExitCode, string Output)> RunProcess(
    string workingDirectory,
    string executable,
    params string[] arguments
)
{
    var start = new ProcessStartInfo(executable)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var argument in arguments)
        start.ArgumentList.Add(argument);
    using var process =
        Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, await outputTask + await errorTask);
}

static string TemporaryDirectory()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "bpp-remote-data-tests-" + Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(path);
    return path;
}

static string RepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        directory = directory.Parent;
    return directory?.FullName
        ?? throw new InvalidOperationException("Could not locate repository root.");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}");
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void False(bool condition, string message) => True(!condition, message);

internal sealed class StaticHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _content;

    internal StaticHandler(HttpStatusCode status, string content)
    {
        _status = status;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_content) });
}

internal sealed class TransientThenSuccessHandler(string content) : HttpMessageHandler
{
    internal int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        if (RequestCount == 1)
            throw new HttpRequestException("connection reset");

        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }
        );
    }
}

internal sealed class TransientStatusThenSuccessHandler(string content) : HttpMessageHandler
{
    internal int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        return Task.FromResult(
            RequestCount == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content),
                }
        );
    }
}

internal sealed class AlwaysTransientHandler : HttpMessageHandler
{
    internal int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        throw new HttpRequestException("connection reset");
    }
}

internal sealed class CountingStatusHandler(HttpStatusCode status) : HttpMessageHandler
{
    internal int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        return Task.FromResult(new HttpResponseMessage(status));
    }
}

internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string[] _responses;
    private readonly HttpStatusCode _status;
    private readonly CancellationTokenSource _shutdown = new();

    internal LoopbackServer(
        string firstResponse,
        string secondResponse,
        HttpStatusCode status = HttpStatusCode.OK
    )
        : this(new[] { firstResponse, secondResponse }, status) { }

    internal LoopbackServer(IEnumerable<string> responses, HttpStatusCode status)
    {
        _responses = responses.ToArray();
        _status = status;
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        BaseUrl = $"http://127.0.0.1:{endpoint.Port}/";
        Completion = ServeAsync();
    }

    internal string BaseUrl { get; }
    internal List<string> Requests { get; } = new();
    internal Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        try
        {
            await Completion;
        }
        catch (Exception) when (_shutdown.IsCancellationRequested) { }
        _shutdown.Dispose();
    }

    private async Task ServeAsync()
    {
        for (var i = 0; i < _responses.Length; i++)
        {
            using var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var request = new StringBuilder();
            while (await reader.ReadLineAsync() is { } line && line.Length > 0)
                request.AppendLine(line);
            Requests.Add(request.ToString());

            var body = Encoding.UTF8.GetBytes(_responses[i]);
            var reason = _status == HttpStatusCode.OK ? "OK" : "Bad Gateway";
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)_status} {reason}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"
            );
            await stream.WriteAsync(headers);
            await stream.WriteAsync(body);
        }
    }
}
