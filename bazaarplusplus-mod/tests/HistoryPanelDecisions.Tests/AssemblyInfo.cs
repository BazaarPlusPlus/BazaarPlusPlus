using Xunit;

// The localization facade L is process-global; every test class installs the language the
// capsule ran its checks under, and several checks switch it. Run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
