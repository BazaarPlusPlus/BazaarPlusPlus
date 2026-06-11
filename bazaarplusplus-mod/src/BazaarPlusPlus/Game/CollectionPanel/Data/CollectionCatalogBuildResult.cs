#nullable enable
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCatalogBuildResult
{
    public CollectionCatalogBuildResult(
        IReadOnlyList<CollectionCardVm> cards,
        int sourceTemplateCount,
        int scannedCount,
        int acceptedCount,
        int rejectedCount,
        bool wasCacheHit
    )
    {
        Cards = cards;
        SourceTemplateCount = sourceTemplateCount;
        ScannedCount = scannedCount;
        AcceptedCount = acceptedCount;
        RejectedCount = rejectedCount;
        WasCacheHit = wasCacheHit;
    }

    public IReadOnlyList<CollectionCardVm> Cards { get; }
    public int SourceTemplateCount { get; }
    public int ScannedCount { get; }
    public int AcceptedCount { get; }
    public int RejectedCount { get; }
    public bool WasCacheHit { get; }
}
