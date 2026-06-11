#nullable enable
namespace BazaarPlusPlus.Core.Events;

internal sealed class RunInitializedObserved
{
    public string RunId { get; set; } = string.Empty;
}
