#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMount : IBppMountable
{
    private readonly Func<CombatReplayRuntime?> _combatReplayRuntime;
    private readonly Func<ModOnlineClient?> _onlineClient;
    private IDisposable? _localeChangedSubscription;

    public HistoryPanelMount(
        Func<CombatReplayRuntime?> combatReplayRuntime,
        Func<ModOnlineClient?> onlineClient
    )
    {
        _combatReplayRuntime = combatReplayRuntime;
        _onlineClient = onlineClient;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var combatReplayRuntime = _combatReplayRuntime();
        if (combatReplayRuntime == null)
        {
            BppLog.Warn("HistoryPanelMount", "CombatReplayRuntime unavailable; skipping mount.");
            return;
        }

        var panel = host.AddComponent<HistoryPanel>();
        var runtime = new HistoryPanelRuntime(
            services.RunContext,
            services.Paths.RunLogDatabasePath,
            services.Paths.CombatReplayDirectoryPath,
            services.Paths.CombatReplayVideoDirectoryPath,
            services.Paths.PluginsDirectoryPath,
            () => combatReplayRuntime
        );

        var onlineClient = _onlineClient();
        if (onlineClient == null)
        {
            BppLog.Warn(
                "HistoryPanelMount",
                "Online client unavailable; HistoryPanel left unconfigured."
            );
            return;
        }

        panel.Configure(HistoryPanelFactory.Create(runtime, onlineClient));

        _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
            HistoryPanel.RefreshLocalization()
        );
    }

    public void Unmount(GameObject host)
    {
        _localeChangedSubscription?.Dispose();
        _localeChangedSubscription = null;

        var panel = host.GetComponent<HistoryPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
