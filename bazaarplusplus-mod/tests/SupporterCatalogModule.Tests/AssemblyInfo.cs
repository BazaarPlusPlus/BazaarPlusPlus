using Xunit;

// The BPPSupporterCatalog facade is process-global; the tests reset it and read what the module
// published. Run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
