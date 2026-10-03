#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal sealed class CollectionCardArtLogState
{
    private readonly HashSet<CollectionPanelLogReasonCode> _reportedReasons = [];

    internal void ReportDegraded(
        CollectionPanelLogReasonCode reasonCode,
        CollectionCardArtStatus status,
        string? artKey,
        Exception? exception
    )
    {
        if (!_reportedReasons.Add(reasonCode))
            return;

        var fields = new BppLogField[]
        {
            ("reason_code", reasonCode),
            ("status", status),
            ("art_key", artKey),
        };
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.card_art.degraded",
                    storm: ["reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.card_art.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                fields
            );
    }
}
