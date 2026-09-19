#nullable enable

RoutesTests.Run();
CodecTests.Run();
await ModApiResponseTests.RunAsync();
await SessionTests.RunAsync();
HealthClientTests.Run();
BazaarDbLinkClientTests.Run();
Console.WriteLine("All ModApi tests passed.");
