#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.Paths;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelMount : IBppMountable
{
    private readonly Func<CombatReplayRuntime?> _combatReplayRuntime;
    private readonly Func<ModApiSession?> _modApiSession;
    private readonly Func<BazaarDbLinkClient?> _accountLinkClient;
    private readonly Func<DesktopOverlayPanels?> _overlayHost;
    private IDisposable? _localeChangedSubscription;
    private Touch.TouchHistoryPanel? _touchPanel;

    public HistoryPanelMount(
        Func<CombatReplayRuntime?> combatReplayRuntime,
        Func<ModApiSession?> modApiSession,
        Func<BazaarDbLinkClient?> accountLinkClient,
        Func<DesktopOverlayPanels?> overlayHost
    )
    {
        _combatReplayRuntime = combatReplayRuntime;
        _modApiSession = modApiSession;
        _accountLinkClient = accountLinkClient;
        _overlayHost = overlayHost;
    }

    public void Mount(GameObject host, IBppServices services)
    {
        var combatReplayRuntime = _combatReplayRuntime();
        var overlayHost = _overlayHost();
        var modApiSession = _modApiSession();
        var plan = HistoryPanelMountPlan.Resolve(
            combatReplayRuntime != null,
            overlayHost != null,
            modApiSession != null
        );
        if (plan == HistoryPanelMountMode.DoNotMount && combatReplayRuntime == null)
        {
            LogMissingDependency(HistoryPanelMountDependency.CombatReplayRuntime);
            return;
        }

        if (plan == HistoryPanelMountMode.DoNotMount)
        {
            LogMissingDependency(HistoryPanelMountDependency.OverlayPanelHost);
            return;
        }

        // CombatReplayRuntime accessor is not a path and is not on services.Paths — pass it
        // straight through to Factory. Paths are startup-stable strings.
        var dependencies = HistoryPanelFactory.Create(
            services.RunContext,
            modApiSession,
            () => combatReplayRuntime,
            PathConstants.RunLogDatabase(services.Paths.RequireDataRoot()),
            PathConstants.CombatReplays(services.Paths.RequireDataRoot()),
            PathConstants.CombatReplayVideos(services.Paths.RequireDataRoot()),
            _accountLinkClient(),
            () =>
                HistoryPanelDecisions.ResolveAccountLinkGate(
                    services.Config.BazaarDbUploadEnabled.Value,
                    services.GameBuild.Channel
                )
        );
        _localeChangedSubscription = services.EventBus.Subscribe<ChineseLocaleModeChanged>(_ =>
            HistoryPanel.RefreshLocalization()
        );
        // A mobile platform (ADR-0024) draws the Android host's touch History in place of the
        // desktop panel, over the same dependencies and panel id.
        if (TouchPlatform.IsMobile)
        {
            _touchPanel = new Touch.TouchHistoryPanel(dependencies, overlayHost!);
            return;
        }

        var panel = host.AddComponent<HistoryPanel>();
        panel.Configure(dependencies);
        // Register with the host only once fully configured; an unconfigured panel (skip paths
        // above) must stay invisible to overlay lifecycle routing.
        panel.AttachToOverlayHost(overlayHost!);
    }

    private static void LogMissingDependency(HistoryPanelMountDependency dependency)
    {
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.mount.failed"),
            ("dependency", dependency),
            ("reason_code", HistoryPanelMountReasonCode.DependencyUnavailable)
        );
    }

    public void Unmount(GameObject host)
    {
        _localeChangedSubscription?.Dispose();
        _localeChangedSubscription = null;
        _touchPanel?.Dispose();
        _touchPanel = null;

        var panel = host.GetComponent<HistoryPanel>();
        if (panel != null)
            UnityEngine.Object.DestroyImmediate(panel);
    }
}
