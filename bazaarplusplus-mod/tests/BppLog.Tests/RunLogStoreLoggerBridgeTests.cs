#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.RunLogging;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.RunLog;
using Xunit;

namespace RunLoggingQueueLogging.Tests;

public sealed class RunLogStoreLoggerBridgeTests
{
    [Fact]
    public void Bridge_maps_all_storage_variants_to_locked_RunLogging_events()
    {
        BppLog.Reset();
        var bridge = new RunLogStoreLoggerBridge();
        var writeFailure = new InvalidOperationException("write failed");
        var workerFailure = new InvalidOperationException("worker failed");

        bridge.Emit(
            RunLogStoreDiagnostic.ShutdownDrainTimedOut(TimeSpan.FromMilliseconds(2500), 4)
        );
        bridge.Emit(
            RunLogStoreDiagnostic.WriteFailed(
                RunLogStoreWriteOperation.SaveCheckpoint,
                "run-123456789",
                writeFailure
            )
        );
        bridge.Emit(RunLogStoreDiagnostic.WorkerFailed(7, workerFailure));

        Assert.Collection(
            BppLog.Events,
            item =>
            {
                Assert.Equal("Warning", item.Severity);
                Assert.Equal("run_logging.queue.shutdown_degraded", item.Definition.EventId);
                Assert.Same(BppLogFeatureScope.RunLogging, item.Definition.Scope);
                AssertFields(
                    item,
                    ("timeout_ms", 2500L),
                    ("pending_count", 4),
                    ("reason_code", RunLoggingReasonCode.QueueShutdownDrainTimeout)
                );
            },
            item =>
            {
                Assert.Equal("Error", item.Severity);
                Assert.Equal("run_logging.queue.write_failed", item.Definition.EventId);
                Assert.Same(writeFailure, item.Exception);
                AssertFields(
                    item,
                    ("run_id", "run-123456789"),
                    ("operation", RunLogStoreWriteOperation.SaveCheckpoint),
                    ("reason_code", RunLoggingReasonCode.QueueWriteException)
                );
            },
            item =>
            {
                Assert.Equal("Error", item.Severity);
                Assert.Equal("run_logging.queue.worker_failed", item.Definition.EventId);
                Assert.Same(workerFailure, item.Exception);
                AssertFields(
                    item,
                    ("pending_count", 7),
                    ("reason_code", RunLoggingReasonCode.QueueWorkerTerminatedUnexpectedly)
                );
            }
        );
    }

    [Fact]
    public void Queue_events_match_the_locked_53_catalog()
    {
        var definitions = typeof(RunLoggingLogEvents)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!)
            .Where(definition => definition.EventId.StartsWith("run_logging.queue."))
            .ToDictionary(definition => definition.EventId);

        Assert.Equal(3, definitions.Count);
        Assert.Equal(
            "timeout_ms:Low:None|pending_count:High:None|reason_code:Low:None",
            Describe(definitions["run_logging.queue.shutdown_degraded"])
        );
        Assert.Equal(
            "run_id:High:Short|operation:Low:None|reason_code:Low:None",
            Describe(definitions["run_logging.queue.write_failed"])
        );
        Assert.Equal(
            "pending_count:High:None|reason_code:Low:None",
            Describe(definitions["run_logging.queue.worker_failed"])
        );
        Assert.All(
            definitions.Values,
            definition => Assert.Same(BppLogFeatureScope.RunLogging, definition.Scope)
        );
        Assert.Equal(
            new[] { "reason_code" },
            definitions["run_logging.queue.shutdown_degraded"]
                .StormPolicy!.KeyFields.Select(field => field.Name)
        );
        Assert.Empty(definitions["run_logging.queue.write_failed"].StormPolicy!.KeyFields);
        Assert.Empty(definitions["run_logging.queue.worker_failed"].StormPolicy!.KeyFields);

        var validation = BppLogEventCatalog
            .FromDefinitions(definitions.Values.ToArray())
            .Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    private static void AssertFields(
        CapturedRunLoggingQueueEvent item,
        params (string Name, object Value)[] expected
    )
    {
        Assert.Equal(expected.Length, item.Values.Length);
        Assert.Equal(
            expected,
            item.Values.Select(value => (value.Field.Name, value.Value!)).ToArray()
        );
    }

    private static string Describe(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Cardinality}:{field.Correlation}"
            )
        );
}
