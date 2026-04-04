#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BazaarPlusPlus.Game.ModApi;

internal interface IModApiAuthenticatedResult
{
    bool Succeeded { get; }

    string? Error { get; }

    bool ShouldFallback { get; }

    bool ShouldReRegister { get; }
}

internal readonly struct ModApiAuthenticatedRequestResult<TResult>
    where TResult : struct, IModApiAuthenticatedResult
{
    private ModApiAuthenticatedRequestResult(bool registrationAvailable, TResult response)
    {
        RegistrationAvailable = registrationAvailable;
        Response = response;
    }

    public bool RegistrationAvailable { get; }

    public TResult Response { get; }

    public static ModApiAuthenticatedRequestResult<TResult> RegistrationUnavailable() =>
        new(false, default);

    public static ModApiAuthenticatedRequestResult<TResult> Success(TResult response) =>
        new(true, response);
}

internal sealed class ModApiAuthenticatedSession
{
    private readonly ModApiRegistrationClient _registrationClient;
    private readonly ModApiClientStateStore _clientStateStore;

    public ModApiAuthenticatedSession(
        ModApiRegistrationClient registrationClient,
        ModApiClientStateStore clientStateStore
    )
    {
        _registrationClient =
            registrationClient ?? throw new ArgumentNullException(nameof(registrationClient));
        _clientStateStore =
            clientStateStore ?? throw new ArgumentNullException(nameof(clientStateStore));
    }

    public async Task<ModApiAuthenticatedRequestResult<TResult>> SendAsync<TResult>(
        string installId,
        Func<string, CancellationToken, Task<TResult>> sendAsync,
        CancellationToken cancellationToken
    )
        where TResult : struct, IModApiAuthenticatedResult
    {
        if (sendAsync == null)
            throw new ArgumentNullException(nameof(sendAsync));

        var clientId = await _registrationClient.EnsureClientRegistrationAsync(
            installId,
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(clientId))
            return ModApiAuthenticatedRequestResult<TResult>.RegistrationUnavailable();

        var response = await sendAsync(clientId, cancellationToken);
        if (!response.Succeeded && response.ShouldReRegister)
        {
            _clientStateStore.ClearClientId();
            clientId = await _registrationClient.EnsureClientRegistrationAsync(
                installId,
                cancellationToken
            );
            if (string.IsNullOrWhiteSpace(clientId))
                return ModApiAuthenticatedRequestResult<TResult>.RegistrationUnavailable();

            response = await sendAsync(clientId, cancellationToken);
        }

        return ModApiAuthenticatedRequestResult<TResult>.Success(response);
    }
}
