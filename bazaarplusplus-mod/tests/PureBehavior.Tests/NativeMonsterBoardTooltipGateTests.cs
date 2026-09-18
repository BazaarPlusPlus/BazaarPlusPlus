using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using Xunit;

public sealed class NativeMonsterBoardTooltipGateTests
{
    [Fact]
    public async Task Closing_during_native_spawn_rejects_late_show_without_rejecting_new_tooltips()
    {
        var gate = new NativeMonsterBoardTooltipGate();
        var oldData = new object();
        var oldLease = gate.Register(oldData);
        var spawned = new TaskCompletionSource();
        var lateShow = ShowAfterSpawn();
        oldLease.Retire();
        var newData = new object();
        gate.Register(newData);
        spawned.SetResult();
        Assert.False(await lateShow);
        Assert.True(gate.Allows(newData));
        Assert.True(gate.Allows(new object()));

        async Task<bool> ShowAfterSpawn()
        {
            await spawned.Task;
            return gate.Allows(oldData);
        }
    }

    [Fact]
    public void Rebinding_pooled_data_is_independent_of_the_old_lease()
    {
        var gate = new NativeMonsterBoardTooltipGate();
        var data = new object();
        var oldLease = gate.Register(data);
        var current = gate.Register(data);
        oldLease.Retire();
        Assert.True(gate.Allows(data));
        current.Retire();
        Assert.False(gate.Allows(data));
    }
}
