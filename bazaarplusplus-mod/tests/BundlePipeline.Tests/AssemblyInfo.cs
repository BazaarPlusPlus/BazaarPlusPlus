using Xunit;

// BppLog sinks and SQLite connection pools are shared across tests in this process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
