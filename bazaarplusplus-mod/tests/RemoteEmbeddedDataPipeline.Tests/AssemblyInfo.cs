using Xunit;

// Run serially because the child MSBuild and shell processes build projects in the same checkout.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
