#nullable enable
namespace BazaarPlusPlus.Core.Events;

/// <summary>
/// Run Logging has written a Run as completed: its <c>runs</c> row is committed, so the Run can be
/// Seal-Eligible from now on. Published under Run Logging's lifecycle lock, on the main thread or
/// on the deferred-completion timer's thread pool thread, so a handler must not block.
/// </summary>
public sealed class RunLogRunCompleted
{
    public string RunId { get; set; } = string.Empty;
}
