#nullable enable

SqliteShutdownTests.Run();
RunLogCheckpointTests.Run();
RunLogHistoryOnlyResumeTests.Run();
RunLogSchemaInitializationTests.Run();
RunLogSchemaIndexTests.Run();
RunLogSchemaColumnShapeTests.Run();
SqliteStoreConnectionTests.Run();
RunLogEventPayloadTests.Run();
RunLogSchemaReleaseContractTests.Run();

Console.WriteLine("All Storage tests passed.");
