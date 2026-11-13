using Xunit;

// The workflow checks install a BppLog sink, which is process-global. Run the tests one at a
// time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
