using Xunit;

// Each check clears the process-wide SQLite connection pools when it drops its database. Run
// the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
