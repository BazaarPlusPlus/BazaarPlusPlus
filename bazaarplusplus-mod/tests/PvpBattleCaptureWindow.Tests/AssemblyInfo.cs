using Xunit;

// BppLog is process-wide: each test installs its own sink, so tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
