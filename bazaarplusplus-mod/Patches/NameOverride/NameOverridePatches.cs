#pragma warning disable CS0436
#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Patches;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus;

internal static class NameOverrideHelper
{
    private const string ReplacementName = "Anonymous";

    public static bool IsEnabled()
    {
        return BppPatchHost.Services.Config.EnableNameOverrideConfig?.Value == true;
    }

    public static bool TryGetDisplayNameOverride(string originalName, out string? replacementName)
    {
        replacementName = null;

        if (!IsEnabled())
            return false;

        replacementName = ReplacementName;
        return !string.Equals(originalName, replacementName, StringComparison.Ordinal);
    }

    public static bool TryGetReplacementName(string originalName, out string? replacementName)
    {
        replacementName = null;

        if (!IsEnabled())
            return false;

        var profileName = BppClientCacheBridge.TryGetProfileUsername();
        if (string.IsNullOrEmpty(profileName))
        {
            BppLog.Debug(
                "NameOverride",
                "Skipping replacement because profile username is unavailable"
            );
            return false;
        }

        if (!string.Equals(originalName, profileName, StringComparison.Ordinal))
            return false;

        replacementName = ReplacementName;
        return true;
    }
}

[HarmonyPatch]
public static class PlayerProfileGetDisplayUsernamePatch
{
    private static MethodBase? TargetMethod()
    {
        var playerProfileType = AccessTools.TypeByName("TheBazaar.PlayerProfile");
        return playerProfileType == null
            ? null
            : AccessTools.Method(playerProfileType, "GetDisplayUsername");
    }

    [HarmonyPostfix]
    private static void Postfix(ref string __result)
    {
        if (!NameOverrideHelper.TryGetDisplayNameOverride(__result, out var replacementName))
            return;

        __result = replacementName!;
        BppLog.Debug(
            "NameOverride",
            $"GetDisplayUsername replaced display username with {replacementName}"
        );
    }
}

[HarmonyPatch(
    typeof(HeroBannerController),
    "UpdatePlayer",
    new[]
    {
        typeof(string),
        typeof(int),
        typeof(string),
        typeof(TheBazaar.ProfileData.ISeasonRank),
        typeof(int?),
    }
)]
public static class UpdatePlayerPatch
{
    [HarmonyPrefix]
    static bool Prefix(
        HeroBannerController __instance,
        ref string userName,
        ref int nameId,
        ref string titlePrefix,
        TheBazaar.ProfileData.ISeasonRank currentSeasonRank,
        int? leaderboardPosition
    )
    {
        if (!NameOverrideHelper.TryGetReplacementName(userName, out var replacementName))
            return true;

        userName = replacementName!;
        nameId = 0;
        BppLog.Debug("NameOverride", $"UpdatePlayer replaced username with {replacementName}");
        return true;
    }
}

[HarmonyPatch(typeof(HeroBannerController), "SetHeroName")]
public static class SetHeroNamePatch
{
    [HarmonyPrefix]
    static bool Prefix(ref string newName, ref int usernameId)
    {
        if (!NameOverrideHelper.TryGetReplacementName(newName, out var replacementName))
            return true;

        newName = replacementName!;
        usernameId = 0;
        BppLog.Debug("NameOverride", $"SetHeroName replaced username with {replacementName}");
        return true;
    }
}
