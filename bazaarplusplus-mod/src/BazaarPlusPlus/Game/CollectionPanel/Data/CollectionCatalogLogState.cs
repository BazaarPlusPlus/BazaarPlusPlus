#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

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
        BppLogField field = ("reason_code", reasonCode);
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.catalog.degraded",
                    storm: ["reason_code"]
                ),
                field
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.catalog.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                field
            );
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
                    new BppLogEvent(
                        BppLogFeatureScope.CollectionPanel,
                        "collection_panel.catalog.degraded",
                        storm: ["reason_code"]
                    ),
                    ("reason_code", _firstReason.Value)
                );
            }
            _state = CatalogState.Ready;
            _firstReason = null;
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.catalog.recovered"
                ),
                ("accepted_count", acceptedCount),
                ("rejected_count", rejectedCount),
                ("source_template_count", sourceTemplateCount)
            );
            return;
        }

        _state = CatalogState.Ready;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.CollectionPanel, "collection_panel.catalog.ready"),
            ("accepted_count", acceptedCount),
            ("rejected_count", rejectedCount),
            ("source_template_count", sourceTemplateCount)
        );
    }

    internal void ReportInvalidated(CollectionPanelLogReasonCode reasonCode)
    {
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CollectionPanel,
                "collection_panel.catalog.invalidated"
            ),
            () => [("reason_code", reasonCode)]
        );
    }

    private enum CatalogState
    {
        Waiting,
        Ready,
        Degraded,
    }
}
