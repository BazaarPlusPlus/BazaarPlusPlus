#nullable enable

RoutesTests.Run();
ErrorCodeContractTests.Run();
CodecTests.Run();
await ModApiResponseTests.RunAsync();
await SessionTests.RunAsync();
HealthClientTests.Run();
BazaarDbLinkClientTests.Run();
Console.WriteLine("All ModApi tests passed.");
