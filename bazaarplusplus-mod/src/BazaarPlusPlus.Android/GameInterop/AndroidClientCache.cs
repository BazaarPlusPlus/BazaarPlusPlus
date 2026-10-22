using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The signed-in player's profile and rank from the game's <c>ClientCache</c>, the Android
/// counterpart of the desktop's <c>BppClientCacheBridge</c>. A cache without data reads as absent.
/// </summary>
internal static class AndroidClientCache
{
    public static string? TryGetProfileAccountId()
    {
        try
        {
            var profile = ClientCache.Profile;
            if (profile == null || !profile.HasData)
                return null;
            var accountId = profile.Value?.AccountId.ToString();
            return string.IsNullOrWhiteSpace(accountId) ? null : accountId;
        }
        catch
        {
            return null;
        }
    }

    public static string? TryGetProfileUsername()
    {
        try
        {
            var profile = ClientCache.Profile;
            if (profile == null || !profile.HasData)
                return null;
            var username = profile.Value?.Username;
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryGetRank(out string? rank, out int? rating)
    {
        rank = null;
        rating = null;
        try
        {
            var observable = ClientCache.Rank;
            if (observable == null || !observable.HasData)
                return false;
            var response = observable.Value;
            rank = response.Rank.ToString();
            rating = response.Rating;
            return !string.IsNullOrWhiteSpace(rank) || rating.HasValue;
        }
        catch
        {
            rank = null;
            rating = null;
            return false;
        }
    }
}
