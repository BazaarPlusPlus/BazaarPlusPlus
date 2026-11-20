using Xunit;

// Run serially because the tests reset the process-global BPPSupporterCatalog facade.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
