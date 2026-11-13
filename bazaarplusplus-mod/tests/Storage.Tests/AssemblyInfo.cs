using Xunit;

// Every test ends with SqliteConnection.ClearAllPools(), which clears the process-wide pool
// another test may be using, so the tests run one at a time as the console runner did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
