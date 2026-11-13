using Xunit;

// The localization facade L is process-global; the test class installs it and one test resets it.
// Run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
