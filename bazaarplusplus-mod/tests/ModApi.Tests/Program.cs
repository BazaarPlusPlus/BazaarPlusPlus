#nullable enable

ErrorCodeContractTests.Run();
await GhostSummaryContractTests.RunAsync();
CodecTests.Run();
await ModApiResponseTests.RunAsync();
await SessionTests.RunAsync();
await HealthClientTests.RunAsync();
BazaarDbLinkClientTests.Run();
Console.WriteLine("All ModApi tests passed.");
