using Il2CppBazaarGameShared.TempoNet.Models;
using Il2CppInterop.Runtime;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

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

    // The end-of-run screen refreshes the cached rank after the server applies the Run's rating
    // change.
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
