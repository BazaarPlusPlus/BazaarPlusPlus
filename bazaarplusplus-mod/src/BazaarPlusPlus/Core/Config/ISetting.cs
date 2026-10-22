#nullable enable
namespace BazaarPlusPlus.Core.Config;

/// <summary>
/// One persisted setting. It carries no change event: consumers that cache derived state compare
/// the raw <see cref="Value"/> against the value they last saw.
/// </summary>
internal interface ISetting<T>
{
    T Value { get; set; }
}
