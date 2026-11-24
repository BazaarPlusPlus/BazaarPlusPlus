using System.Reflection;
using BazaarPlusPlus.Core.GameAuth;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>Reads the official game session credential and client headers via IL2CPP interop on the main thread.</summary>
internal static class AndroidGameAuthBridge
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
            {
                BppLog.DebugEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.game_auth.unavailable"),
                    () => [("has_token", !string.IsNullOrWhiteSpace(token))]
                );
                return null;
            }
            return new BppGameAuthSnapshot(token!.Trim(), platform!.Trim(), flavor!.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadAccessToken()
    {
        var instance = GameInstance.Instance;
        if (instance == null)
            return null;

        var instanceType = instance.GetType();
        var provider =
            instanceType.GetProperty("AccessTokenProvider", AnyInstance)?.GetValue(instance, null)
            ?? instanceType
                .GetProperty("_accessTokenProvider", AnyInstance)
                ?.GetValue(instance, null)
            ?? instanceType.GetField("_accessTokenProvider", AnyInstance)?.GetValue(instance);
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
        var type = typeof(GameInstance).Assembly.GetType("Il2CppCore.Data.Providers.DataProvider");
        var method = type?.GetMethod("GetPlatform", AnyStatic, null, Type.EmptyTypes, null);
        return method?.Invoke(null, null) as string;
    }

    private static string? TryReadClientFlavor()
    {
        var type = typeof(GameInstance).Assembly.GetType("Il2CppCore.Data.Providers.DataProvider");
        var method = type?.GetMethod("GetClientFlavor", AnyStatic, null, Type.EmptyTypes, null);
        return method?.Invoke(null, null)?.ToString();
    }
}
