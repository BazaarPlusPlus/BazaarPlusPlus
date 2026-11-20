using Xunit;

// Run serially because the tests count SQLite commands and clear process-wide connection pools.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
