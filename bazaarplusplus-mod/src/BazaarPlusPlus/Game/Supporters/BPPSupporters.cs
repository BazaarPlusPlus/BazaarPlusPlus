#nullable enable
namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporters
{
    private static readonly BPPSupporterAttribution Attribution = new(Environment.TickCount);

    public static IReadOnlyList<BPPSupporterSample> SampleMany(int count) =>
        Attribution.Next(BPPSupporterCatalog.GetCurrentEntries(), count);

    public static BPPSupporterSample Sample()
    {
        return BPPSupporterSampler.Sample(
            BPPSupporterCatalog.GetCurrentEntries(),
            () => UnityEngine.Random.value
        );
    }
}
