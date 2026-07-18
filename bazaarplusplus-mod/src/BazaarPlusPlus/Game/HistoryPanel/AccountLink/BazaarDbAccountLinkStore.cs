#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.AccountLink;

internal sealed class BazaarDbAccountLinkStore
{
    private const string AnonymousAccountScope = "anonymous";
    private const string PrefsKeyPrefix = "BPP.HistoryPanel.BazaarDbLinkedName";

    public void SaveHint(string accountId)
    {
        // Presence-only hint: there is no read-back / unlink endpoint, and the card no longer shows a
        // display name, so we only remember THAT this account linked.
        PlayerPrefs.SetString(BuildPrefsKey(accountId), "1");
        PlayerPrefs.Save();
    }

    public bool IsLinked(string accountId)
    {
        return PlayerPrefs.HasKey(BuildPrefsKey(accountId));
    }

    internal static string BuildPrefsKey(string? accountId)
    {
        var scope = string.IsNullOrWhiteSpace(accountId)
            ? AnonymousAccountScope
            : Uri.EscapeDataString(accountId);
        return $"{PrefsKeyPrefix}.{scope}";
    }
}
