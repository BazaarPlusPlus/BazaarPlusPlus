using Xunit;

// Several test classes install process-global state (L.Install, EncounterPreviewLocalization's
// TextLocalizer) with different values, so test classes must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
