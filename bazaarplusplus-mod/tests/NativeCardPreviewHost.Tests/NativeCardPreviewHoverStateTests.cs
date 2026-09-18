using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeCardPreviewHoverStateTests
{
    [Fact]
    public void Closing_scope_clears_its_hovered_resource_immediately()
    {
        var state = new NativeCardPreviewHoverState<Scope, Resource>();
        var scope = new Scope();
        var resource = new Resource();
        state.Set(scope, resource);

        state.Clear(scope);

        Assert.False(state.TryGet((_, _) => true, out _, out _));
    }

    [Fact]
    public void Inactive_resource_is_never_returned_to_tooltip_refresh()
    {
        var state = new NativeCardPreviewHoverState<Scope, Resource>();
        var scope = new Scope();
        var resource = new Resource();
        state.Set(scope, resource);

        Assert.False(state.TryGet((_, _) => false, out _, out _));
        Assert.False(state.TryGet((_, _) => true, out _, out _));
    }

    private sealed class Scope;

    private sealed class Resource;
}
