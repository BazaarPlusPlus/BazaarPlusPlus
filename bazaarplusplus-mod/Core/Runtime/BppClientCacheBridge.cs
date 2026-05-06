#nullable enable
using System;
using System.Reflection;
using BazaarGameShared.TempoNet.Enums;
using BazaarGameShared.TempoNet.Models;
using BazaarGameShared.TempoNet.Responses;
using HarmonyLib;

namespace BazaarPlusPlus.Core.Runtime;

internal static class BppClientCacheBridge
{
    public static string? TryGetProfileUsername()
    {
        var profile = TryGetProfileValue();
        return profile == null ? null : ReadStringMember(profile, "Username");
    }

    public static string? TryGetProfileAccountId()
    {
        var profile = TryGetProfileValue();
        if (profile == null)
            return null;

        var accountId = ReadMember(profile, "AccountId");
        return accountId?.ToString();
    }

    public static string? TryGetProfileDisplayUsername()
    {
        var profile = TryGetProfileValue();
        if (profile == null)
            return null;

        try
        {
            var method = AccessTools.Method(profile.GetType(), "GetDisplayUsername");
            var displayName = method?.Invoke(profile, null) as string;
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
        }
        catch { }

        return ReadStringMember(profile, "Username");
    }

    public static bool TryGetPlayerRankSnapshot(
        out string? rank,
        out int? rating,
        out ERank? rankEnum
    )
    {
        rank = null;
        rating = null;
        rankEnum = null;

        if (!TryGetObservableValue("Rank", out var hasData, out var value) || !hasData)
            return false;

        if (value is PlayerRankResponse response)
        {
            rankEnum = response.Rank;
            rating = response.Rating;
            rank = response.Rank.ToString();
            return !string.IsNullOrWhiteSpace(rank) || rating.HasValue;
        }

        var rankValue = ReadMember(value, "Rank");
        if (rankValue is ERank directRank)
        {
            rankEnum = directRank;
            rank = directRank.ToString();
        }
        else if (rankValue != null && Enum.TryParse(rankValue.ToString(), out ERank parsedRank))
        {
            rankEnum = parsedRank;
            rank = parsedRank.ToString();
        }

        rating = ReadNullableIntMember(value, "Rating");
        return !string.IsNullOrWhiteSpace(rank) || rating.HasValue;
    }

    public static bool TryGetPlayerLeaderboardPosition(out int? position)
    {
        position = null;

        if (!TryGetObservableValue("Leaderboard", out var hasData, out var value) || !hasData)
            return false;

        position =
            ReadNullableIntMember(value, "position") ?? ReadNullableIntMember(value, "Position");
        return position.HasValue;
    }

    private static object? TryGetProfileValue()
    {
        return TryGetObservableValue("Profile", out _, out var value) ? value : null;
    }

    private static bool TryGetObservableValue(
        string memberName,
        out bool hasData,
        out object? value
    )
    {
        hasData = false;
        value = null;

        try
        {
            var clientCacheType = AccessTools.TypeByName("TheBazaar.ClientCache");
            if (clientCacheType == null)
                return false;

            var observable = ReadStaticMember(clientCacheType, memberName);
            if (observable == null)
                return false;

            hasData = ReadBoolMember(observable, "HasData") ?? false;
            value = ReadMember(observable, "Value");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? ReadStaticMember(Type type, string memberName)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        return type.GetProperty(memberName, flags)?.GetValue(null, null)
            ?? type.GetField(memberName, flags)?.GetValue(null);
    }

    private static object? ReadMember(object? instance, string memberName)
    {
        if (instance == null)
            return null;

        var type = instance.GetType();
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;
        return type.GetProperty(memberName, flags)?.GetValue(instance, null)
            ?? type.GetField(memberName, flags)?.GetValue(instance);
    }

    private static string? ReadStringMember(object? instance, string memberName)
    {
        return ReadMember(instance, memberName) as string;
    }

    private static bool? ReadBoolMember(object? instance, string memberName)
    {
        var value = ReadMember(instance, memberName);
        if (value is bool boolValue)
            return boolValue;

        return value == null ? null
            : bool.TryParse(value.ToString(), out var parsed) ? parsed
            : null;
    }

    private static int? ReadNullableIntMember(object? instance, string memberName)
    {
        var value = ReadMember(instance, memberName);
        if (value is int intValue)
            return intValue;

        return value == null ? null
            : int.TryParse(value.ToString(), out var parsed) ? parsed
            : null;
    }
}
