using Xunit;

// The checks count SQLite commands and work per payload, and clear the process-wide SQLite
// connection pools between databases. Run the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
