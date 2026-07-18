#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMount : IBppMountable
{
    private readonly Func<CombatReplayRuntime?> _combatReplayRuntime;
    private readonly Func<ModOnlineClient?> _onlineClient;
    private readonly Func<BazaarDbLinkClient?> _accountLinkClient;
    private readonly Func<OverlayPanelHost?> _overlayHost;
    private readonly INativeCardPreviewHost _nativeCardPreviewHost;
    private IDisposable? _localeChangedSubscription;

    public HistoryPanelMount(
        Func<CombatReplayRuntime?> combatReplayRuntime,
        Func<ModOnlineClient?> onlineClient,
        Func<BazaarDbLinkClient?> accountLinkClient,
        Func<OverlayPanelHost?> overlayHost,
        INativeCardPreviewHost nativeCardPreviewHost
    )
    {
        _combatReplayRuntime = combatReplayRuntime;
        _onlineClient = onlineClient;
        _accountLinkClient = accountLinkClient;
        _overlayHost = overlayHost;
        _nativeCardPreviewHost =
            nativeCardPreviewHost ?? throw new ArgumentNullException(nameof(nativeCardPreviewHost));
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var combatReplayRuntime = _combatReplayRuntime();
        if (combatReplayRuntime == null)
        {
            LogMissingDependency(HistoryPanelMountDependency.CombatReplayRuntime);
            return;
        }

        var overlayHost = _overlayHost();
        if (overlayHost == null)
        {
            LogMissingDependency(HistoryPanelMountDependency.OverlayPanelHost);
            return;
        }

        var onlineClient = _onlineClient();
        if (onlineClient == null)
        {
            LogMissingDependency(HistoryPanelMountDependency.OnlineClient);
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

        panel.Configure(
            HistoryPanelFactory.Create(
                runtime,
                onlineClient,
                _accountLinkClient(),
                () =>
                    HistoryPanelDecisions.IsAccountLinkCardAvailable(
                        services.Config.BazaarDbUploadEnabled?.Value ?? false,
                        services.GameBuild.Channel
                    )
            ),
            _nativeCardPreviewHost
        );
        // Register with the host only once fully configured; an unconfigured panel (skip paths
        // above) must stay invisible to overlay lifecycle routing.
        panel.AttachToOverlayHost(overlayHost);

        _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
            HistoryPanel.RefreshLocalization()
        );
    }

    private static void LogMissingDependency(HistoryPanelMountDependency dependency)
    {
        BppLog.ErrorEvent(
            HistoryPanelLogEvents.MountFailed,
            HistoryPanelLogEvents.MountDependency.Bind(dependency),
            HistoryPanelLogEvents.MountReasonCode.Bind(
                HistoryPanelMountReasonCode.DependencyUnavailable
            )
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
