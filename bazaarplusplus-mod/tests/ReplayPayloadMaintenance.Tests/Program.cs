#nullable enable
using Microsoft.Data.Sqlite;

try
{
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
