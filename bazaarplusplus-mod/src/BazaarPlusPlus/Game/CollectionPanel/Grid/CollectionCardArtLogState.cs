#nullable enable
using BazaarPlusPlus.Infrastructure;

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

        var fields = new[]
        {
            CollectionPanelLogEvents.CardArtDegradedReasonCode.Bind(reasonCode),
            CollectionPanelLogEvents.CardArtDegradedStatus.Bind(status),
            CollectionPanelLogEvents.CardArtDegradedArtKey.Bind(artKey),
        };
        if (exception == null)
            BppLog.WarnEvent(CollectionPanelLogEvents.CardArtDegraded, fields);
        else
            BppLog.WarnEvent(CollectionPanelLogEvents.CardArtDegraded, exception, fields);
    }
}
