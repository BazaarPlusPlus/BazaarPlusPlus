using Xunit;

// Run serially because the sponsor action test changes process-global localization.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
