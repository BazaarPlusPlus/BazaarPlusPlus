#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCatalogLogState
{
    private CatalogState _state;
    private CollectionPanelLogReasonCode? _firstReason;

    internal void ReportDegraded(CollectionPanelLogReasonCode reasonCode, Exception? exception)
    {
        if (_state == CatalogState.Degraded)
            return;

        _state = CatalogState.Degraded;
        _firstReason = reasonCode;
        var field = CollectionPanelLogEvents.CatalogDegradedReasonCode.Bind(reasonCode);
        if (exception == null)
            BppLog.WarnEvent(CollectionPanelLogEvents.CatalogDegraded, field);
        else
            BppLog.WarnEvent(CollectionPanelLogEvents.CatalogDegraded, exception, field);
    }

    internal void ReportBuilt(int acceptedCount, int rejectedCount, int sourceTemplateCount)
    {
        if (_state == CatalogState.Ready)
            return;

        if (_state == CatalogState.Degraded)
        {
            if (_firstReason.HasValue)
            {
                BppLog.RecoverStorm(
                    CollectionPanelLogEvents.CatalogDegraded,
                    CollectionPanelLogEvents.CatalogDegradedReasonCode.Bind(_firstReason.Value)
                );
            }
            _state = CatalogState.Ready;
            _firstReason = null;
            BppLog.InfoEvent(
                CollectionPanelLogEvents.CatalogRecovered,
                CollectionPanelLogEvents.CatalogRecoveredAcceptedCount.Bind(acceptedCount),
                CollectionPanelLogEvents.CatalogRecoveredRejectedCount.Bind(rejectedCount),
                CollectionPanelLogEvents.CatalogRecoveredSourceTemplateCount.Bind(
                    sourceTemplateCount
                )
            );
            return;
        }

        _state = CatalogState.Ready;
        BppLog.InfoEvent(
            CollectionPanelLogEvents.CatalogReady,
            CollectionPanelLogEvents.CatalogReadyAcceptedCount.Bind(acceptedCount),
            CollectionPanelLogEvents.CatalogReadyRejectedCount.Bind(rejectedCount),
            CollectionPanelLogEvents.CatalogReadySourceTemplateCount.Bind(sourceTemplateCount)
        );
    }

    internal void ReportInvalidated(CollectionPanelLogReasonCode reasonCode)
    {
        BppLog.DebugEvent(
            CollectionPanelLogEvents.CatalogInvalidated,
            () => [CollectionPanelLogEvents.CatalogInvalidatedReasonCode.Bind(reasonCode)]
        );
    }

    private enum CatalogState
    {
        Waiting,
        Ready,
        Degraded,
    }
}
