#nullable enable
using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Game.Identity;

internal static class PlayerAccountIdResolver
{
    internal static string? ResolveCurrent()
    {
        try
        {
            var cached = BppClientCacheBridge.TryGetProfileAccountId()?.Trim();
            return string.IsNullOrWhiteSpace(cached) ? null : cached;
        }
        catch
        {
            return null;
        }
    }
}
