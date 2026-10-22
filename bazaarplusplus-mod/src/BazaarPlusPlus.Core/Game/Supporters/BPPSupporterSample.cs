#nullable enable

namespace BazaarPlusPlus.Game.Supporters;

public readonly struct BPPSupporterSample
{
    public string Name { get; init; }

    public int Tier { get; init; }

    public bool HasValue => !string.IsNullOrWhiteSpace(Name) && Tier > 0;
}
