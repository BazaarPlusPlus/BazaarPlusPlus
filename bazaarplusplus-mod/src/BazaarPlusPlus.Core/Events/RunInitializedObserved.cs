#nullable enable
namespace BazaarPlusPlus.Core.Events;

public sealed class RunInitializedObserved
{
    public string RunId { get; set; } = string.Empty;
}
