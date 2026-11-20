using Xunit;

// Run serially because the Pantry test inspects the game's process-global serializer type registry.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
