#nullable enable
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace CollectionPanelLogging.Tests;

public sealed class CollectionPanelStateTests
{
    public CollectionPanelStateTests() => BppLog.Reset();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Selection_aggregates_simultaneous_failures_and_recovers_only_after_a_complete_open(
        int failureCount
    )
    {
        var state = new CollectionPanelSelectionLogState();
        var first = new InvalidOperationException("first");
        var failures = new[]
        {
            new CollectionPanelSelectionProbeFailure(
                CollectionPanelSelectionProbe.RunState,
                CollectionPanelLogReasonCode.ProbeReadFailed,
                first
            ),
            new CollectionPanelSelectionProbeFailure(
                CollectionPanelSelectionProbe.Hero,
                CollectionPanelLogReasonCode.ProbeReadFailed,
                new Exception("hero")
            ),
            new CollectionPanelSelectionProbeFailure(
                CollectionPanelSelectionProbe.Day,
                CollectionPanelLogReasonCode.ProbeReadFailed,
                new Exception("day")
            ),
            new CollectionPanelSelectionProbeFailure(
                CollectionPanelSelectionProbe.Encounter,
                CollectionPanelLogReasonCode.ProbeReadFailed,
                new Exception("encounter")
            ),
        };

        state.ObserveOpen(
            CollectionPanelSelectionOpenObservation.Degraded(failures.Take(failureCount).ToArray())
        );
        state.ObserveOpen(
            CollectionPanelSelectionOpenObservation.Degraded(
                failures.Reverse().Take(failureCount).ToArray()
            )
        );
        state.ObserveOpen(CollectionPanelSelectionOpenObservation.Incomplete());

        var warning = Assert.Single(BppLog.Events);
        Assert.Equal("Warning", warning.Severity);
        Assert.Same(first, warning.Exception);
        Assert.Equal(CollectionPanelSelectionProbe.RunState, Value(warning, "probe"));

        state.ObserveOpen(CollectionPanelSelectionOpenObservation.Complete());
        state.ObserveOpen(CollectionPanelSelectionOpenObservation.Complete());

        Assert.Equal(2, BppLog.Events.Count);
        var recovery = BppLog.Events[1];
        Assert.Equal("Info", recovery.Severity);
        Assert.Equal(CollectionPanelSelectionProbe.RunState, Value(recovery, "probe"));
        Assert.Single(BppLog.Recoveries);
    }

    [Fact]
    public void Catalog_ready_is_process_state_not_cache_state()
    {
        var state = new CollectionCatalogLogState();

        state.ReportBuilt(acceptedCount: 10, rejectedCount: 2, sourceTemplateCount: 12);
        state.ReportBuilt(acceptedCount: 10, rejectedCount: 2, sourceTemplateCount: 12);
        state.ReportInvalidated(CollectionPanelLogReasonCode.LocaleChange);
        state.ReportBuilt(acceptedCount: 11, rejectedCount: 2, sourceTemplateCount: 13);

#if DEBUG
        Assert.Equal(2, BppLog.Events.Count);
        Assert.Equal("collection_panel.catalog.ready", BppLog.Events[0].Definition.EventId);
        Assert.Equal("collection_panel.catalog.invalidated", BppLog.Events[1].Definition.EventId);
#else
        Assert.Single(BppLog.Events);
        Assert.Equal("collection_panel.catalog.ready", BppLog.Events[0].Definition.EventId);
#endif
    }

    [Fact]
    public void Catalog_fault_or_null_warns_once_until_a_successful_build_then_recovers_once()
    {
        var state = new CollectionCatalogLogState();
        var first = new InvalidOperationException("task");

        state.ReportDegraded(CollectionPanelLogReasonCode.CardMapTaskFailed, first);
        state.ReportDegraded(CollectionPanelLogReasonCode.CardMapNull, null);
        state.ReportBuilt(4, 1, 5);
        state.ReportBuilt(4, 1, 5);

        Assert.Equal(2, BppLog.Events.Count);
        Assert.Equal("Warning", BppLog.Events[0].Severity);
        Assert.Equal("collection_panel.catalog.degraded", BppLog.Events[0].Definition.EventId);
        Assert.Same(first, BppLog.Events[0].Exception);
        Assert.Equal("Info", BppLog.Events[1].Severity);
        Assert.Equal("collection_panel.catalog.recovered", BppLog.Events[1].Definition.EventId);
        Assert.Single(BppLog.Recoveries);
    }

    [Fact]
    public void Layout_warns_once_across_blocker_churn_then_recovers_once()
    {
        var state = new CollectionPanelDockLayoutLogState();
        state.Observe(CollectionPanelDockLayoutObservation.Available());
        state.Observe(
            CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.PlacementBlocked,
                "Root\nOne"
            )
        );
        state.Observe(
            CollectionPanelDockLayoutObservation.Degraded(
                CollectionPanelLogReasonCode.AnchorCanvasUnavailable,
                null
            )
        );
        state.Observe(CollectionPanelDockLayoutObservation.Available());
        state.Observe(CollectionPanelDockLayoutObservation.Available());

        Assert.Equal(2, BppLog.Events.Count);
        Assert.Equal("collection_panel.dock_layout.degraded", BppLog.Events[0].Definition.EventId);
        Assert.Equal("Root\nOne", Value(BppLog.Events[0], "blocker"));
        Assert.Equal("collection_panel.dock_layout.recovered", BppLog.Events[1].Definition.EventId);
        Assert.Equal(
            CollectionPanelLogReasonCode.PlacementBlocked,
            Value(BppLog.Events[1], "reason_code")
        );
        Assert.Single(BppLog.Recoveries);
    }

    [Fact]
    public void Art_warns_once_per_cause_not_once_per_hostile_key()
    {
        var state = new CollectionCardArtLogState();
        state.ReportDegraded(
            CollectionPanelLogReasonCode.AddressablesLoadException,
            CollectionCardArtStatus.ArtUnavailable,
            "a\nsecret",
            new Exception("one")
        );
        state.ReportDegraded(
            CollectionPanelLogReasonCode.AddressablesLoadException,
            CollectionCardArtStatus.ArtUnavailable,
            "b\rsecret",
            new Exception("two")
        );
        state.ReportDegraded(
            CollectionPanelLogReasonCode.AddressablesLoadFailed,
            CollectionCardArtStatus.ArtUnavailable,
            "c\tsecret",
            null
        );

        Assert.Equal(2, BppLog.Events.Count);
        Assert.Equal("a\nsecret", Value(BppLog.Events[0], "art_key"));
        Assert.Equal("c\tsecret", Value(BppLog.Events[1], "art_key"));
    }

    [Fact]
    public void Load_diagnostics_is_a_real_compile_time_lazy_Debug_boundary()
    {
        var clockReads = 0;
        long Clock()
        {
            clockReads++;
            return 100 + clockReads;
        }
        var diagnostics = new CollectionPanelLoadDiagnostics(Clock);

        diagnostics.Complete(
            CollectionPanelLoadPhase.PanelLoad,
            CollectionPanelLoadOutcome.Loaded,
            null
        );

#if DEBUG
        Assert.Equal(2, clockReads);
        Assert.Single(BppLog.Events);
        Assert.Equal("Debug", BppLog.Events[0].Severity);
#else
        Assert.Equal(0, clockReads);
        Assert.Empty(BppLog.Events);
#endif
    }

    private static object? Value(CapturedBppLogEvent logEvent, string fieldName) =>
        logEvent.Values.Single(value => value.Field.Name == fieldName).Value;
}
