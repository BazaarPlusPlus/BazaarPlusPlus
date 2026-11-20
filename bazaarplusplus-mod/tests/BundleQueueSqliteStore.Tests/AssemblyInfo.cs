using Xunit;

// Database cleanup clears SQLite connection pools across the process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
