using Xunit;

// Cleanup clears SQLite connection pools across the process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
