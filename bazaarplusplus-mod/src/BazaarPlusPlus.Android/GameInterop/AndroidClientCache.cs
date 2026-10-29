using Il2CppBazaarGameShared.TempoNet.Models;
using Il2CppInterop.Runtime;
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

    /// <summary>
    /// Calls <paramref name="onRefreshed"/> each time the game replaces the cached rank, as the
    /// end-of-run screen does once the server has applied the Run's rating change. Main thread.
    /// </summary>
    public static IDisposable SubscribeRankRefreshed(Action<string?, int?> onRefreshed)
    {
        var listener =
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action<PlayerRankResponse>>(
                new Action<PlayerRankResponse>(response =>
                    onRefreshed(response.Rank.ToString(), response.Rating)
                )
            ) ?? throw new InvalidOperationException("Il2CppInterop returned no delegate.");
        ClientCache.Rank.Subscribe(listener);
        return new RankSubscription(listener);
    }

    private sealed class RankSubscription(Il2CppSystem.Action<PlayerRankResponse> listener)
        : IDisposable
    {
        public void Dispose() => ClientCache.Rank.Unsubscribe(listener);
    }
}
