#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace HistoryPanelOperationalLogging.Tests;

public sealed class HistoryPanelPayloadFailureLogGateTests : IDisposable
{
    public HistoryPanelPayloadFailureLogGateTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Unchanged_corruption_logs_once_while_changed_or_cleared_state_reopens()
    {
        var gate = new HistoryPanelPayloadFailureLogGate();

        gate.Report(
            "battle-private",
            "fingerprint-a",
            HistoryPanelPreviewPayloadReasonCode.PayloadInvalid,
            null
        );
        gate.Report(
            "battle-private",
            "fingerprint-a",
            HistoryPanelPreviewPayloadReasonCode.PayloadInvalid,
            null
        );
        gate.Report(
            "battle-private",
            "fingerprint-b",
            HistoryPanelPreviewPayloadReasonCode.PayloadInvalid,
            null
        );
        gate.Clear("battle-private");
        gate.Report(
            "battle-private",
            "fingerprint-b",
            HistoryPanelPreviewPayloadReasonCode.PayloadUnreadable,
            new IOException("read failed")
        );

        Assert.Equal(3, BppLog.Events.Count);
        Assert.All(
            BppLog.Events,
            captured =>
                Assert.Equal("history_panel.preview.payload_degraded", captured.Definition.EventId)
        );
        Assert.Equal("Warning", BppLog.Events[0].Severity);
        Assert.Null(BppLog.Events[0].Exception);
        Assert.IsType<IOException>(BppLog.Events[2].Exception);
    }

    [Fact]
    public void Correlation_state_is_bounded()
    {
        var gate = new HistoryPanelPayloadFailureLogGate();

        for (var index = 0; index < 300; index++)
        {
            gate.Report(
                $"battle-{index:00000000}",
                $"fingerprint-{index}",
                HistoryPanelPreviewPayloadReasonCode.PayloadInvalid,
                null
            );
        }

        Assert.True(gate.Count <= 256);
    }
}
