#nullable enable
using BazaarPlusPlus.ModApi;

internal static class RoutesTests
{
    public static void Run()
    {
        var routes =
            ModApiRoutes.TryCreate("https://mod-api-v5.bazaarplusplus.com")
            ?? throw new InvalidOperationException("TryCreate returned null for valid URL");

        if (routes.UploadBundle != "https://mod-api-v5.bazaarplusplus.com/bundles")
            throw new InvalidOperationException($"Unexpected UploadBundle: {routes.UploadBundle}");

        if (routes.QueryGhostBattles != "https://mod-api-v5.bazaarplusplus.com/ghost-battles")
            throw new InvalidOperationException(
                $"Unexpected QueryGhostBattles: {routes.QueryGhostBattles}"
            );

        if (routes.Health != "https://mod-api-v5.bazaarplusplus.com/health")
            throw new InvalidOperationException($"Unexpected Health: {routes.Health}");

        if (ModApiRoutes.TryCreate(null) != null)
            throw new InvalidOperationException("TryCreate must return null for null input");
        if (ModApiRoutes.TryCreate("not-a-url") != null)
            throw new InvalidOperationException("TryCreate must return null for unparseable input");
    }
}
