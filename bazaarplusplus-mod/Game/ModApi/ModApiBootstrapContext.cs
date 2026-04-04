#nullable enable
using System;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiBootstrapContext
{
    private ModApiBootstrapContext(
        string databasePath,
        string? replayRootPath,
        string identityPath,
        string clientStatePath,
        string privateKeyPath,
        ModApiRoutes routes
    )
    {
        DatabasePath = databasePath;
        ReplayRootPath = replayRootPath;
        IdentityPath = identityPath;
        ClientStatePath = clientStatePath;
        PrivateKeyPath = privateKeyPath;
        Routes = routes;
    }

    public string DatabasePath { get; }

    public string? ReplayRootPath { get; }

    public string IdentityPath { get; }

    public string ClientStatePath { get; }

    public string PrivateKeyPath { get; }

    public ModApiRoutes Routes { get; }

    public ModApiIdentityStore CreateIdentityStore()
    {
        return new ModApiIdentityStore(IdentityPath);
    }

    public ModApiClientStateStore CreateClientStateStore()
    {
        return new ModApiClientStateStore(ClientStatePath);
    }

    public ModApiKeyStore CreateKeyStore()
    {
        return new ModApiKeyStore(PrivateKeyPath);
    }

    public static ModApiBootstrapContext? TryCreate(
        string? databasePath,
        string? replayRootPath,
        string? identityPath,
        string? clientStatePath,
        string? privateKeyPath,
        string? apiBaseUrl
    )
    {
        if (
            string.IsNullOrWhiteSpace(databasePath)
            || string.IsNullOrWhiteSpace(identityPath)
            || string.IsNullOrWhiteSpace(clientStatePath)
            || string.IsNullOrWhiteSpace(privateKeyPath)
        )
        {
            return null;
        }

        var routes = ModApiRoutes.TryCreate(apiBaseUrl);
        if (routes == null)
            return null;

        return new ModApiBootstrapContext(
            databasePath.Trim(),
            string.IsNullOrWhiteSpace(replayRootPath) ? null : replayRootPath.Trim(),
            identityPath.Trim(),
            clientStatePath.Trim(),
            privateKeyPath.Trim(),
            routes
        );
    }
}
