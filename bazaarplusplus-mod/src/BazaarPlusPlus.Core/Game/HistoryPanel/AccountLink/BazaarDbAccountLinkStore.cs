#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.AccountLink;

public sealed class BazaarDbAccountLinkStore
{
    private const string AnonymousAccountScope = "anonymous";
    private const string PrefsKeyPrefix = "BPP.HistoryPanel.BazaarDbLinkedName";

    private readonly Func<string, bool> _hasKey;
    private readonly Action<string> _markKey;

    public BazaarDbAccountLinkStore(Func<string, bool> hasKey, Action<string> markKey)
    {
        _hasKey = hasKey ?? throw new ArgumentNullException(nameof(hasKey));
        _markKey = markKey ?? throw new ArgumentNullException(nameof(markKey));
    }

    public void SaveHint(string accountId)
    {
        // The API has no read-back or unlink endpoint, so this local key is only a link hint.
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
