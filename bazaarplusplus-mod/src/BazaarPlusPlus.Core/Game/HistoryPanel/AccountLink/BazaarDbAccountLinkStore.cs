#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.AccountLink;

public sealed class BazaarDbAccountLinkStore
{
    private const string AnonymousAccountScope = "anonymous";
    private const string PrefsKeyPrefix = "BPP.HistoryPanel.BazaarDbLinkedName";

    private readonly Func<string, bool> _hasKey;
    private readonly Action<string> _markKey;

    // The host's key-value store, keyed by BuildPrefsKey: the desktop passes Unity's PlayerPrefs.
    public BazaarDbAccountLinkStore(Func<string, bool> hasKey, Action<string> markKey)
    {
        _hasKey = hasKey ?? throw new ArgumentNullException(nameof(hasKey));
        _markKey = markKey ?? throw new ArgumentNullException(nameof(markKey));
    }

    public void SaveHint(string accountId)
    {
        // Presence-only hint: there is no read-back / unlink endpoint, and the card no longer shows a
        // display name, so we only remember THAT this account linked.
        _markKey(BuildPrefsKey(accountId));
    }

    public bool IsLinked(string accountId)
    {
        return _hasKey(BuildPrefsKey(accountId));
    }

    internal static string BuildPrefsKey(string? accountId)
    {
        var scope = string.IsNullOrWhiteSpace(accountId)
            ? AnonymousAccountScope
            : Uri.EscapeDataString(accountId);
        return $"{PrefsKeyPrefix}.{scope}";
    }
}
