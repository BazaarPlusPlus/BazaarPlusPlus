using Xunit;

// Several test classes install process-global localization state (L.Install) with
// different languages and locale modes, and most others read it indirectly through
// CollectionPanelText, so test classes must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
