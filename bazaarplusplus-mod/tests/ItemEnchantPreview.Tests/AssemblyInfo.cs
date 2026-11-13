using Xunit;

// The tests load the Desktop Host assembly, whose preview cache and log sink are process-global.
// Run the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
