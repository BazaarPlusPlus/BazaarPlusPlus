using Xunit;

// The game's serializer type registry is process-global, and the Pantry test checks that the
// adapter leaves it alone. Run the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
