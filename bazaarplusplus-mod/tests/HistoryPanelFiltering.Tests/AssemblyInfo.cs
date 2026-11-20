using Xunit;

// The profile fake and L both hold process-wide state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
