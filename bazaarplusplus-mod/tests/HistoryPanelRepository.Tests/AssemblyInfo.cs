using Xunit;

// The capsule ran its checks one after another, and each class's cleanup clears SQLite's
// process-wide connection pools. Run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
