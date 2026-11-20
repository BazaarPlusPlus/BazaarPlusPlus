using Xunit;

// Run serially because the tests replace the global BppLog sink and clear the SQLite pool.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
