using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Android.Supporters;

/// <summary>The composition-owned supporter catalog and rotation shared by touch panels.</summary>
internal sealed class AndroidSupporterData
{
    private readonly SupporterCatalogModule _catalog;
    private readonly BPPSupporterAttribution _attribution = new(Environment.TickCount);
    private bool _stopped;

    public AndroidSupporterData(BppSettings settings, string dataRoot)
    {
        BPPSupporterCatalog.Install(settings);
        _catalog = new SupporterCatalogModule(
            () => settings.UseFixedSupporterListConfig.Value,
            observer => AndroidSupporterCatalog.Create(dataRoot, observer),
            static () => DateTime.UtcNow,
            SupporterCatalogSettings.BuildCacheFilePath(dataRoot)
        );
    }

    public void Warm()
    {
        if (!_stopped)
            _catalog.Start();
    }

    public IReadOnlyList<BPPSupporterSample> Next(int count) =>
        !_stopped && BPPSupporterCatalog.HasEntries
            ? _attribution.Next(BPPSupporterCatalog.GetCurrentEntries(), count)
            : Array.Empty<BPPSupporterSample>();

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        _catalog.Stop();
        BPPSupporterCatalog.Reset();
    }
}
