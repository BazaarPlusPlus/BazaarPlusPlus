using Xunit;

// The pipeline and seal failure checks install BppLog sinks and read what they caught, and the
// checks clear the process-wide SQLite connection pools. Run the tests one at a time, as the
// capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
