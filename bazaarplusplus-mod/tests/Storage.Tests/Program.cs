#nullable enable

TempDirPathProviderTests.Run();
SqliteShutdownTests.Run();
RunLogSchemaMigrationTests.Run();
RunLogSchemaIndexTests.Run();
SqliteStoreConnectionTests.Run();
RunLogEventPayloadTests.Run();
RunLogSchemaReleaseContractTests.Run();

Console.WriteLine("All Storage tests passed.");
