#nullable enable
using Microsoft.Data.Sqlite;

try
{
    if (args.Length == 1 && args[0] == "--benchmark")
    {
        ReplayPayloadMaintenanceServiceTests.Benchmark();
        ReplayVideoMetadataLifecycleTests.Benchmark();
        Console.WriteLine("Replay payload maintenance benchmark passed.");
        return;
    }
    ReplayMaintenanceStorageTests.Run();
    ReplayPayloadRetentionPolicyTests.Run();
    ReplayPayloadOperationGateTests.Run();
    ReplayPayloadMaintenanceServiceTests.Run();
    ReplayVideoMetadataLifecycleTests.Run();
    ReplayVideoArtifactMaintenanceTests.Run();

    Console.WriteLine("Replay payload maintenance tests passed.");
}
finally
{
    SqliteConnection.ClearAllPools();
}
