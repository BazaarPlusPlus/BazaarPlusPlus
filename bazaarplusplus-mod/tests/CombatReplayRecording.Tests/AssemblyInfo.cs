using Xunit;

// Finalizer scenarios share the Desktop Host's recording lifecycle.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
