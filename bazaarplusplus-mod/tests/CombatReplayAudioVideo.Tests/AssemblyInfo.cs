using Xunit;

// The recording operation checks install a BppLog sink, which is process-global, and read what
// it caught. Run the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
