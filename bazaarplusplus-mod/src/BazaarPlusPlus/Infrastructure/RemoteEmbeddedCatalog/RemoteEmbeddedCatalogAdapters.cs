#nullable enable
using System.Reflection;
using System.Text;

namespace BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

internal sealed class FileCatalogCache : ILocalCatalogCache
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _filePath;

    internal FileCatalogCache(string filePath)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? throw new ArgumentException("A cache file path is required.", nameof(filePath))
            : filePath;
    }

    internal string FilePath => _filePath;

    public ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken) =>
        new(
            Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(_filePath))
                        return (CatalogCacheDocument?)null;

                    var document = File.ReadAllText(_filePath, Utf8NoBom);
                    var lastWriteUtc = File.GetLastWriteTimeUtc(_filePath);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new CatalogCacheDocument(document, lastWriteUtc);
                },
                cancellationToken
            )
        );

    public ValueTask WriteAsync(string document, CancellationToken cancellationToken) =>
        new(
            Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AtomicFileWriter.Write(
                        _filePath,
                        tempPath => File.WriteAllText(tempPath, document, Utf8NoBom)
                    );
                    cancellationToken.ThrowIfCancellationRequested();
                },
                cancellationToken
            )
        );
}

internal sealed class AssemblyResourceCatalogSource : IEmbeddedCatalogSource
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly Assembly _assembly;
    private readonly string _resourceName;

    internal AssemblyResourceCatalogSource(Assembly assembly, string resourceName)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _resourceName = string.IsNullOrWhiteSpace(resourceName)
            ? throw new ArgumentException("A resource name is required.", nameof(resourceName))
            : resourceName;
    }

    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) =>
        new(
            Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = _assembly.GetManifestResourceStream(_resourceName);
                    if (stream == null)
                        return null;

                    using var reader = new StreamReader(
                        stream,
                        Utf8NoBom,
                        detectEncodingFromByteOrderMarks: false
                    );
                    var document = reader.ReadToEnd();
                    cancellationToken.ThrowIfCancellationRequested();
                    return document;
                },
                cancellationToken
            )
        );
}

internal sealed class HttpRemoteCatalogSource : IRemoteCatalogSource
{
    private readonly HttpClient _client;
    private readonly string _url;

    internal HttpRemoteCatalogSource(HttpClient client, string url)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _url = string.IsNullOrWhiteSpace(url)
            ? throw new ArgumentException("A remote URL is required.", nameof(url))
            : url;
    }

    public async ValueTask<string?> DownloadAsync(CancellationToken cancellationToken)
    {
        using var response = await _client
            .GetAsync(_url, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return document;
    }
}

internal sealed class SystemCatalogClock : ICatalogClock
{
    internal static readonly SystemCatalogClock Instance = new();

    private SystemCatalogClock() { }

    public DateTime UtcNow => DateTime.UtcNow;
}

internal sealed class ThreadPoolCatalogRefreshScheduler : ICatalogRefreshScheduler
{
    internal static readonly ThreadPoolCatalogRefreshScheduler Instance = new();

    private ThreadPoolCatalogRefreshScheduler() { }

    public void Queue(Func<Task> refresh)
    {
        if (refresh == null)
            throw new ArgumentNullException(nameof(refresh));
        _ = Task.Run(refresh);
    }
}
