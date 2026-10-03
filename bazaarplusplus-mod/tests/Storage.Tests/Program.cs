#nullable enable

SqliteShutdownTests.Run();
RunLogCheckpointTests.Run();
RunLogSchemaMigrationTests.Run();
RunLogSchemaIndexTests.Run();
RunLogSchemaColumnShapeTests.Run();
SqliteStoreConnectionTests.Run();
RunLogEventPayloadTests.Run();
RunLogSchemaReleaseContractTests.Run();

Console.WriteLine("All Storage tests passed.");
