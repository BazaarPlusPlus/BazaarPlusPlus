using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class UiSuppressionScopeTests
{
    [Fact]
    public void Begin_AppliesSuppressionsInOrder_AndRestoresInReverseOrder()
    {
        var events = new List<string>();

        using var scope = UiSuppressionScope.Begin(
            () => CreateLease("dock", events),
            () => CreateLease("combat", events)
        );

        Assert.Equal(["apply:dock", "apply:combat"], events);

        scope.Dispose();

        Assert.Equal(["apply:dock", "apply:combat", "restore:combat", "restore:dock"], events);
    }

    [Fact]
    public void Begin_RestoresPreviouslyAppliedSuppressions_WhenLaterSuppressionThrows()
    {
        var events = new List<string>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            UiSuppressionScope.Begin(
                () => CreateLease("dock", events),
                () => throw new InvalidOperationException("boom")
            )
        );

        Assert.Equal("boom", exception.Message);
        Assert.Equal(["apply:dock", "restore:dock"], events);
    }

    private static IDisposable CreateLease(string name, ICollection<string> events)
    {
        events.Add($"apply:{name}");
        return new TestLease(() => events.Add($"restore:{name}"));
    }

    private sealed class TestLease(Action onDispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            onDispose();
        }
    }
}
