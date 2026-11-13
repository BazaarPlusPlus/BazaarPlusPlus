using Xunit;

// The MSBuild and fetch-data checks start dotnet and scripts/build.sh, which build and test
// projects of this repository. Run the tests one at a time, as the capsule did.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
