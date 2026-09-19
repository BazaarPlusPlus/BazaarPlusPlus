using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace BazaarPlusPlus.RemoteEmbeddedDataFetcher;

internal readonly record struct RemoteConnectionAttempt(
    string Host,
    IPAddress Address,
    int Attempt
);

internal static class RemoteEmbeddedDataHttpClient
{
    internal static HttpClient Create(TimeSpan timeout)
    {
        var connector = new RotatingDnsConnector(attempt =>
            Console.Error.WriteLine(
                $"[BazaarPlusPlus] Remote embedded data connection host={attempt.Host} address={attempt.Address} attempt={attempt.Attempt}"
            )
        );
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = connector.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };
        return new HttpClient(handler) { Timeout = timeout };
    }
}

internal sealed class RotatingDnsConnector
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPAddress, int, CancellationToken, ValueTask<Stream>> _connect;
    private readonly Action<RemoteConnectionAttempt>? _observeAttempt;
    private readonly ConcurrentDictionary<string, IPAddress[]> _addressesByHost = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<string, int> _nextAddressByHost = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal RotatingDnsConnector(Action<RemoteConnectionAttempt>? observeAttempt = null)
        : this(Dns.GetHostAddressesAsync, ConnectSocketAsync, observeAttempt) { }

    internal RotatingDnsConnector(
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connect,
        Action<RemoteConnectionAttempt>? observeAttempt = null
    )
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _observeAttempt = observeAttempt;
    }

    internal ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    ) => ConnectAsync(context.DnsEndPoint, cancellationToken);

    internal async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        CancellationToken cancellationToken
    )
    {
        var addresses = await ResolveAddressesAsync(endpoint.Host, cancellationToken)
            .ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        List<Exception>? failures = null;
        for (var offset = 0; offset < addresses.Length; offset++)
        {
            var sequence = _nextAddressByHost.AddOrUpdate(
                endpoint.Host,
                addValue: 0,
                static (_, current) => current == int.MaxValue ? 0 : current + 1
            );
            var address = addresses[sequence % addresses.Length];
            if ((sequence + 1) % addresses.Length == 0)
                _addressesByHost.TryRemove(endpoint.Host, out _);
            _observeAttempt?.Invoke(
                new RemoteConnectionAttempt(endpoint.Host, address, sequence + 1)
            );
            try
            {
                return await _connect(address, endpoint.Port, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }

        throw new HttpRequestException(
            $"Could not connect to any resolved address for {endpoint.Host}.",
            failures is null ? null : new AggregateException(failures)
        );
    }

    private async Task<IPAddress[]> ResolveAddressesAsync(
        string host,
        CancellationToken cancellationToken
    )
    {
        if (_addressesByHost.TryGetValue(host, out var cached))
            return cached;

        var resolved = (await _resolve(host, cancellationToken).ConfigureAwait(false))
            .Where(IsSupported)
            .Distinct()
            .ToArray();
        return _addressesByHost.GetOrAdd(host, resolved);
    }

    private static bool IsSupported(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork
        || (Socket.OSSupportsIPv6 && address.AddressFamily == AddressFamily.InterNetworkV6);

    private static async ValueTask<Stream> ConnectSocketAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken
    )
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        try
        {
            await socket
                .ConnectAsync(new IPEndPoint(address, port), cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
