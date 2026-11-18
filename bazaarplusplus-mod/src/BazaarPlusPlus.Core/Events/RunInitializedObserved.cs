#nullable enable
namespace BazaarPlusPlus.Core.Events;

public sealed class RunInitializedObserved
{
    public string RunId { get; set; } = string.Empty;

    /// <summary>The game server's build id the run-initialized message carries; null when unread.</summary>
    public string? BuildId { get; set; }
}
