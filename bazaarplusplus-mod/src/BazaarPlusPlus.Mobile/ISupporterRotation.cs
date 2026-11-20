using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Mobile;

internal interface ISupporterRotation
{
    // Main thread.
    void Warm();

    // Never credit the placeholder fallback while the supporter list is loading.
    IReadOnlyList<BPPSupporterSample> Next(int count);
}
