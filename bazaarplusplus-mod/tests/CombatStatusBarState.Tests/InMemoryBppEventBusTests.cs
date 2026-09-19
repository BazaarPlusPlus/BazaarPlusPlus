using BazaarPlusPlus.Core.Events;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class InMemoryBppEventBusTests
{
    [Fact]
    public void Publish_ContinuesAfterHandlerThrows()
    {
        var bus = new InMemoryBppEventBus();
        var secondHandlerCalled = false;

        bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<TestEvent>(_ => secondHandlerCalled = true);

        bus.Publish(new TestEvent());

        Assert.True(secondHandlerCalled);
    }

    private sealed class TestEvent { }
}
