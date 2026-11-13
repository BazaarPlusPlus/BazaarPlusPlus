using Xunit;

// The finalizer scenarios drive the Desktop Host's recording lifecycle and background
// finalizers. Run the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
