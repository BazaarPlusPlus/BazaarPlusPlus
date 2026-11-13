using Xunit;

// The localization facade L is process-global; the sponsor action test installs Taiwan mode and
// then the neutral source again. Run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
