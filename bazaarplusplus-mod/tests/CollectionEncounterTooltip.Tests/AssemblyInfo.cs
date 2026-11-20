using Xunit;

// Tests that install or read process-wide localization state must run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
