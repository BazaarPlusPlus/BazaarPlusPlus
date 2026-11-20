#nullable enable
namespace BazaarPlusPlus.Core.Events;

// The upload worker publishes after the transaction commits. Handlers should schedule work and return.
public sealed class BundleSealJobRecreated
{
    public string RunId { get; set; } = string.Empty;
}
