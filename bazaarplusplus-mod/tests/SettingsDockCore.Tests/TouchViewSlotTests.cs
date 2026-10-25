using BazaarPlusPlus.Core.Ui;
using Xunit;

namespace SettingsDockCore.Tests;

/// <summary>
/// The rule every touch panel's view goes through on a new screen (Collection, Ten-Win Builds,
/// History): the old view is gone before the new one builds, and a build that throws leaves the
/// panel with no view, so it reads closed and the next open builds again.
/// </summary>
public sealed class TouchViewSlotTests
{
    private readonly List<FakeView> _dropped = new();
    private readonly TouchViewSlot<FakeView> _slot;

    public TouchViewSlotTests() => _slot = new TouchViewSlot<FakeView>(_dropped.Add);

    [Fact]
    public void The_first_ensure_builds_and_records_the_key()
    {
        var view = _slot.Ensure("a", () => new FakeView("a"), Unexpected);

        Assert.Same(view, _slot.View);
        Assert.Equal("a", _slot.ScreenKey);
        Assert.True(_slot.IsCurrent("a"));
        Assert.Empty(_dropped);
    }

    [Fact]
    public void Ensure_on_the_same_key_reuses_the_view_without_building_or_dropping()
    {
        var first = _slot.Ensure("a", () => new FakeView("a"), Unexpected);

        var second = _slot.Ensure("a", () => throw new InvalidOperationException(), Unexpected);

        Assert.Same(first, second);
        Assert.Empty(_dropped);
    }

    [Fact]
    public void A_new_key_drops_the_old_view_before_the_new_one_builds()
    {
        var old = _slot.Ensure("a", () => new FakeView("a"), Unexpected);

        var view = _slot.Ensure(
            "b",
            () =>
            {
                Assert.Equal([old!], _dropped);
                Assert.Null(_slot.View);
                Assert.Equal(string.Empty, _slot.ScreenKey);
                return new FakeView("b");
            },
            Unexpected
        );

        Assert.Same(view, _slot.View);
        Assert.True(_slot.IsCurrent("b"));
        Assert.False(_slot.IsCurrent("a"));
    }

    [Fact]
    public void A_build_that_throws_leaves_no_view_and_no_key_and_calls_the_hook_once()
    {
        _slot.Ensure("a", () => new FakeView("a"), Unexpected);
        var thrown = new InvalidOperationException("build");
        var failures = new List<Exception>();

        var view = _slot.Ensure("b", () => throw thrown, failures.Add);

        Assert.Null(view);
        Assert.Null(_slot.View);
        Assert.Equal(string.Empty, _slot.ScreenKey);
        Assert.False(_slot.IsCurrent("a"));
        Assert.False(_slot.IsCurrent("b"));
        Assert.Equal([thrown], failures);
    }

    [Fact]
    public void The_retry_after_a_failed_build_drops_nothing_and_builds_again()
    {
        _slot.Ensure("a", () => new FakeView("a"), Unexpected);
        _slot.Ensure("b", () => throw new InvalidOperationException(), _ => { });

        var view = _slot.Ensure("b", () => new FakeView("b"), Unexpected);

        Assert.Single(_dropped);
        Assert.Same(view, _slot.View);
        Assert.True(_slot.IsCurrent("b"));
    }

    [Fact]
    public void A_hook_that_throws_propagates_and_the_slot_still_holds_no_view()
    {
        Assert.Throws<ArgumentException>(() =>
            _slot.Ensure(
                "a",
                () => throw new InvalidOperationException(),
                _ => throw new ArgumentException("hook")
            )
        );

        Assert.Null(_slot.View);
    }

    [Fact]
    public void Rebuild_on_the_current_key_drops_and_builds()
    {
        var old = _slot.Ensure("a", () => new FakeView("a"), Unexpected);

        var view = _slot.Rebuild("a", () => new FakeView("a"), Unexpected);

        Assert.Equal([old!], _dropped);
        Assert.NotSame(old, view);
        Assert.True(_slot.IsCurrent("a"));
    }

    [Fact]
    public void Drop_forgets_then_drops_and_is_a_no_op_with_no_view()
    {
        _slot.Drop();
        Assert.Empty(_dropped);
        var view = _slot.Ensure("a", () => new FakeView("a"), Unexpected);

        _slot.Drop();
        _slot.Drop();

        Assert.Equal([view!], _dropped);
        Assert.Null(_slot.View);
        Assert.False(_slot.IsCurrent("a"));
    }

    [Fact]
    public void A_drop_that_throws_propagates_with_the_view_already_forgotten()
    {
        var slot = new TouchViewSlot<FakeView>(_ => throw new InvalidOperationException("drop"));
        slot.Ensure("a", () => new FakeView("a"), Unexpected);

        Assert.Throws<InvalidOperationException>(() =>
            slot.Ensure("b", () => new FakeView("b"), Unexpected)
        );

        Assert.Null(slot.View);
        Assert.False(slot.IsCurrent("a"));
    }

    private static void Unexpected(Exception exception) =>
        Assert.Fail($"unexpected build failure: {exception}");

    // A reference type without value equality: each build is a distinct view.
    private sealed class FakeView(string key)
    {
        public string Key { get; } = key;
    }
}
