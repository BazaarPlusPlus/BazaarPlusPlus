using Xunit;

// The localization facade L and the BppLog sink are process-global; several tests install them,
// and the log tests read what the sink caught. Run the tests one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
