using Xunit;

// The capsule's fakes keep static state (the profile observable), and L is process-global.
// Run the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
