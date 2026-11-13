using Xunit;

// BppLog takes one process-wide sink and every test clears the process-wide SQLite pool, so the
// tests run one at a time as the console runner did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
