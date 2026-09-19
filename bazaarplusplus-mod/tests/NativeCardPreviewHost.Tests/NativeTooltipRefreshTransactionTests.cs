using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeTooltipRefreshTransactionTests
{
    [Fact]
    public void Success_commits_replacement_before_in_place_apply()
    {
        var current = new Value("old");
        var replacement = new Value("new");
        var stored = current;
        var calls = new List<string>();

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () =>
            {
                calls.Add("create");
                return replacement;
            },
            value =>
            {
                stored = value;
                calls.Add($"write:{value.Name}");
                return true;
            },
            value =>
            {
                calls.Add($"apply:{value.Name}");
                return true;
            }
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.Refreshed, result.Status);
        Assert.Same(replacement, stored);
        Assert.Equal(new[] { "create", "write:new", "apply:new" }, calls);
    }

    [Fact]
    public void Failed_apply_restores_old_value_and_tooltip()
    {
        var current = new Value("old");
        var stored = current;
        var applied = new List<Value>();

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () => new Value("new"),
            value =>
            {
                stored = value;
                return true;
            },
            value =>
            {
                applied.Add(value);
                return ReferenceEquals(value, current);
            }
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.ApplyFailed, result.Status);
        Assert.Same(current, stored);
        Assert.Equal(new[] { new Value("new"), current }, applied);
    }

    [Fact]
    public void Failed_write_rolls_back_even_if_write_partially_mutated_state()
    {
        var current = new Value("old");
        var stored = current;
        var writeCount = 0;

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () => new Value("new"),
            value =>
            {
                stored = value;
                writeCount++;
                return writeCount > 1;
            },
            _ => throw new InvalidOperationException("must not apply")
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.WriteFailed, result.Status);
        Assert.Same(current, stored);
        Assert.Equal(2, writeCount);
    }

    [Fact]
    public void Failed_rollback_does_not_apply_unrestored_current_value()
    {
        var current = new Value("old");
        var replacement = new Value("new");
        var stored = current;
        var writeCount = 0;
        var applied = new List<Value>();

        var result = NativeTooltipRefreshTransaction.Execute(
            current,
            () => replacement,
            value =>
            {
                writeCount++;
                if (writeCount == 1)
                {
                    stored = value;
                    return true;
                }

                return false;
            },
            value =>
            {
                applied.Add(value);
                return false;
            }
        );

        Assert.Equal(NativeTooltipRefreshTransactionStatus.RollbackFailed, result.Status);
        Assert.Same(replacement, stored);
        Assert.Equal(2, writeCount);
        Assert.Equal(new[] { replacement }, applied);
    }

    private sealed record Value(string Name);
}
