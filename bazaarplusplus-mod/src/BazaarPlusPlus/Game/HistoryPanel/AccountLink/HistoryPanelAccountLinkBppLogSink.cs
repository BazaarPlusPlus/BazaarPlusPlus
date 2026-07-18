#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel.AccountLink;

internal sealed class HistoryPanelAccountLinkBppLogSink : IHistoryPanelAccountLinkLogSink
{
    internal static HistoryPanelAccountLinkBppLogSink Instance { get; } = new();

    private HistoryPanelAccountLinkBppLogSink() { }

    public void Emit(
        BppLogSeverity severity,
        BppLogEventDefinition definition,
        BppLogFieldValue[] values,
        Exception? exception
    )
    {
        switch (severity)
        {
            case BppLogSeverity.Info:
                BppLog.InfoEvent(definition, values);
                break;
            case BppLogSeverity.Warning:
                BppLog.WarnEvent(definition, values);
                break;
            case BppLogSeverity.Error:
                if (exception == null)
                    BppLog.ErrorEvent(definition, values);
                else
                    BppLog.ErrorEvent(definition, exception, values);
                break;
            case BppLogSeverity.Debug:
                BppLog.DebugEvent(definition, () => values);
                break;
        }
    }

    public void EmitDebug(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) => BppLog.DebugEvent(definition, valuesFactory);
}
