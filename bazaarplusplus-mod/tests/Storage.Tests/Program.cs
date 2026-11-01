#nullable enable

SqliteShutdownTests.Run();
RunLogCheckpointTests.Run();
RunLogHistoryOnlyResumeTests.Run();
RunLogUploadPolicyTests.Run();
RunLogRatingDeltaTests.Run();
RunLogSchemaInitializationTests.Run();
RunLogSchemaIndexTests.Run();
RunLogSchemaColumnShapeTests.Run();
SqliteStoreConnectionTests.Run();
IdempotentAccountWriteTests.Run();
RunLogEventPayloadTests.Run();
RunLogSchemaReleaseContractTests.Run();

Console.WriteLine("All Storage tests passed.");
