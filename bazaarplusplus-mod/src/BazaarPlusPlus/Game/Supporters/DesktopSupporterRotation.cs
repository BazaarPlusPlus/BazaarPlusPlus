#nullable enable
namespace BazaarPlusPlus.Game.Supporters;

/// <summary>
/// The Mono host's <see cref="ISupporterRotation"/> for the Mobile UI, over the catalog the
/// composition's Supporter Catalog Module already loads, so <see cref="Warm"/> starts nothing.
/// As on Android, nobody is credited until the list has loaded.
/// </summary>
internal sealed class DesktopSupporterRotation : ISupporterRotation
{
    private readonly BPPSupporterAttribution _attribution = new(Environment.TickCount);

    public void Warm() { }

    public IReadOnlyList<BPPSupporterSample> Next(int count) =>
        BPPSupporterCatalog.HasEntries
            ? _attribution.Next(BPPSupporterCatalog.GetCurrentEntries(), count)
            : Array.Empty<BPPSupporterSample>();
}
