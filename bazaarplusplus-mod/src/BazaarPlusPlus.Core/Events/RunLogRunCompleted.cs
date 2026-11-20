#nullable enable
namespace BazaarPlusPlus.Core.Events;

// The completed row is committed before publication. Handlers must return quickly because
// the publisher holds the lifecycle lock, including on the deferred completion thread.
public sealed class RunLogRunCompleted
{
    public string RunId { get; set; } = string.Empty;
}
