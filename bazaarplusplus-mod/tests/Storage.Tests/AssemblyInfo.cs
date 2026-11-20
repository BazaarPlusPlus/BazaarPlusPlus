using Xunit;

// Run serially because SqliteConnection.ClearAllPools() can clear another test's connection pool.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
