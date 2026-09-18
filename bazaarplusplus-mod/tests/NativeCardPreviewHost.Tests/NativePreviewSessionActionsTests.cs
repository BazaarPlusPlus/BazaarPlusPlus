using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativePreviewSessionActionsTests
{
    [Fact]
    public void Show_hide_and_hover_actions_are_idempotent()
    {
        var actions = new NativePreviewSessionActions();
        var showCalls = 0;
        var hoverCalls = 0;

        Assert.Equal(NativePreviewActionStatus.Applied, actions.SetShown(true, true, Show).Status);
        Assert.Equal(
            NativePreviewActionStatus.AlreadyApplied,
            actions.SetShown(true, true, Show).Status
        );
        Assert.Equal(NativePreviewActionStatus.Applied, actions.SetShown(true, false, Show).Status);
        Assert.Equal(NativePreviewActionStatus.Applied, actions.HoverEnter(true, Hover).Status);
        Assert.Equal(
            NativePreviewActionStatus.AlreadyApplied,
            actions.HoverEnter(true, Hover).Status
        );
        Assert.Equal(NativePreviewActionStatus.Applied, actions.HoverExit(true, Hover).Status);
        Assert.Equal(2, showCalls);
        Assert.Equal(2, hoverCalls);

        NativePreviewActionResult Show()
        {
            showCalls++;
            return Applied();
        }

        NativePreviewActionResult Hover()
        {
            hoverCalls++;
            return Applied();
        }
    }

    [Fact]
    public void Failed_action_does_not_commit_show_or_hover_enter_state()
    {
        var actions = new NativePreviewSessionActions();
        var failure = new NativeCardPreviewFailure(
            NativeCardPreviewOperation.Show,
            NativeCardPreviewFailureReason.ShowException,
            Guid.NewGuid()
        );
        var failed = new NativePreviewActionResult(NativePreviewActionStatus.Failed, failure);

        Assert.Same(failure, actions.SetShown(true, true, () => failed).Failure);
        Assert.Equal(
            NativePreviewActionStatus.Applied,
            actions.SetShown(true, true, Applied).Status
        );
        Assert.Same(failure, actions.HoverEnter(true, () => failed).Failure);
        Assert.Equal(NativePreviewActionStatus.Applied, actions.HoverEnter(true, Applied).Status);
    }

    [Fact]
    public void Dispose_invalidates_every_action_and_reports_prior_hover_state_once()
    {
        var actions = new NativePreviewSessionActions();
        actions.HoverEnter(true, Applied);

        Assert.True(actions.TryDispose(out var wasHovered));
        Assert.True(wasHovered);
        Assert.False(actions.TryDispose(out _));
        Assert.Equal(
            NativePreviewActionStatus.Released,
            actions.SetShown(true, true, Applied).Status
        );
        Assert.Equal(NativePreviewActionStatus.Released, actions.HoverEnter(true, Applied).Status);
    }

    [Fact]
    public void Failed_hover_exit_remains_pending_for_dispose_cleanup()
    {
        var actions = new NativePreviewSessionActions();
        actions.HoverEnter(true, Applied);
        var failure = new NativeCardPreviewFailure(
            NativeCardPreviewOperation.InvokeHoverOut,
            NativeCardPreviewFailureReason.ReflectionException,
            Guid.NewGuid()
        );

        var exit = actions.HoverExit(
            true,
            () => new NativePreviewActionResult(NativePreviewActionStatus.Failed, failure)
        );

        Assert.Same(failure, exit.Failure);
        Assert.True(actions.TryDispose(out var cleanupPending));
        Assert.True(cleanupPending);
    }

    private static NativePreviewActionResult Applied() =>
        new(NativePreviewActionStatus.Applied, null);
}
