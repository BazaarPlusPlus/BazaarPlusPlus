#nullable enable
using System.Reflection;
using BazaarPlusPlus.Core.GameAuth;
using HarmonyLib;

namespace BazaarPlusPlus.GameInterop;

/// <summary>Reads the active official game session credential and client headers on the main thread.</summary>
internal static class BppGameAuthBridge
{
    private const BindingFlags AnyStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static BppGameAuthSnapshot? TryRead()
    {
        try
        {
            var token = TryReadAccessToken();
            var platform = TryReadPlatform();
            var flavor = TryReadClientFlavor();
            if (
                string.IsNullOrWhiteSpace(token)
                || string.IsNullOrWhiteSpace(platform)
                || string.IsNullOrWhiteSpace(flavor)
            )
                return null;
            return new BppGameAuthSnapshot(token!.Trim(), platform!.Trim(), flavor!.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadAccessToken()
    {
        var gameInstanceType = AccessTools.TypeByName("TheBazaar.GameInstance");
        if (gameInstanceType == null)
            return null;

        var instance =
            gameInstanceType.GetProperty("Instance", AnyStatic)?.GetValue(null, null)
            ?? gameInstanceType.GetField("Instance", AnyStatic)?.GetValue(null);
        if (instance == null)
            return null;

        var provider =
            gameInstanceType
                .GetProperty("AccessTokenProvider", AnyInstance)
                ?.GetValue(instance, null)
            ?? gameInstanceType.GetField("_accessTokenProvider", AnyInstance)?.GetValue(instance);
        if (provider == null)
            return null;

        var providerType = provider.GetType();
        var token =
            providerType.GetProperty("AccessToken", AnyInstance)?.GetValue(provider, null)
            ?? providerType.GetField("AccessToken", AnyInstance)?.GetValue(provider);
        return token as string;
    }

    private static string? TryReadPlatform()
    {
        var dataProviderType = AccessTools.TypeByName("Core.Data.Providers.DataProvider");
        var method = dataProviderType?.GetMethod(
            "GetPlatform",
            AnyStatic,
            null,
            Type.EmptyTypes,
            null
        );
        return method?.Invoke(null, null) as string;
    }

    private static string? TryReadClientFlavor()
    {
        var dataProviderType = AccessTools.TypeByName("Core.Data.Providers.DataProvider");
        var method = dataProviderType?.GetMethod(
            "GetClientFlavor",
            AnyStatic,
            null,
            Type.EmptyTypes,
            null
        );
        return method?.Invoke(null, null)?.ToString();
    }
}
