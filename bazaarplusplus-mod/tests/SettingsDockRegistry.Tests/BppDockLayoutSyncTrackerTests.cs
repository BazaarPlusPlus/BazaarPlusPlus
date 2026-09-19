using BazaarPlusPlus.Game.Settings;
using Xunit;

namespace BazaarPlusPlus.Tests.SettingsDockRegistry;

public class BppDockLayoutSyncTrackerTests
{
    [Fact]
    public void Scene_name_change_restarts_immediate_sync_window()
    {
        var tracker = new BppDockLayoutSyncTracker(immediateSyncFrameCount: 2);

        Assert.True(tracker.ShouldSync(sceneName: "MainMenu", realtimeSeconds: 0f));
        Assert.True(tracker.ShouldSync(sceneName: "MainMenu", realtimeSeconds: 0.1f));
        Assert.False(tracker.ShouldSync(sceneName: "MainMenu", realtimeSeconds: 0.2f));
        Assert.True(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 0.2f));
        Assert.True(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 0.3f));
    }

    [Fact]
    public void Delayed_and_steady_probes_continue_for_async_store_content()
    {
        var tracker = new BppDockLayoutSyncTracker(immediateSyncFrameCount: 1);

        Assert.True(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 0f));
        Assert.False(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 0.1f));
        Assert.True(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 0.25f));
        Assert.True(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 8f));
        Assert.False(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 9f));
        Assert.True(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 10f));
        Assert.False(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 11f));
        Assert.True(tracker.ShouldSync(sceneName: "MarketplaceScene", realtimeSeconds: 12f));
    }
}
