#nullable enable
using System.Collections;
using BazaarPlusPlus.Core.GameState;
using Xunit;

namespace Architecture.Tests;

public sealed class EncounterReflectionCollectionTests
{
    [Fact]
    public void Null_or_wrong_typed_reflection_values_are_not_semantic_empty_lists()
    {
        Assert.False(EncounterReflectionCollection.TryGetList(null, out _));
        Assert.False(EncounterReflectionCollection.TryGetList(new object(), out _));
    }

    [Fact]
    public void A_genuine_empty_runtime_list_remains_a_successful_empty_collection()
    {
        IList expected = new ArrayList();

        Assert.True(EncounterReflectionCollection.TryGetList(expected, out var actual));
        Assert.Same(expected, actual);
        Assert.Empty(actual);
    }
}
