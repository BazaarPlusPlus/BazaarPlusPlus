using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeCardPreviewPoolSettlementTests
{
    [Fact]
    public void Preparation_failure_destroys_the_owned_resource()
    {
        var resource = new Resource();
        var destroyed = 0;

        Assert.Throws<InvalidOperationException>(() =>
            NativeCardPreviewPoolSettlement.Prepare(
                resource,
                _ => throw new InvalidOperationException("parent disappeared"),
                _ => throw new InvalidOperationException("must not return"),
                _ => destroyed++,
                CancellationToken.None
            )
        );

        Assert.Equal(1, destroyed);
    }

    [Fact]
    public void Cancellation_return_failure_falls_back_to_destroy_before_propagating()
    {
        var resource = new Resource();
        var destroyed = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<InvalidOperationException>(() =>
            NativeCardPreviewPoolSettlement.Prepare(
                resource,
                _ => { },
                _ => throw new InvalidOperationException("return failed"),
                _ => destroyed++,
                cancellation.Token
            )
        );

        Assert.Equal(1, destroyed);
    }

    [Fact]
    public void Cancellation_returns_exactly_once_then_propagates_cancellation()
    {
        var resource = new Resource();
        var returned = 0;
        var destroyed = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            NativeCardPreviewPoolSettlement.Prepare(
                resource,
                _ => { },
                _ => returned++,
                _ => destroyed++,
                cancellation.Token
            )
        );

        Assert.Equal(1, returned);
        Assert.Equal(0, destroyed);
    }

    private sealed class Resource;
}
