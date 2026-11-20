using Xunit;

// Fixture cleanup clears SQLite connection pools across the process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
