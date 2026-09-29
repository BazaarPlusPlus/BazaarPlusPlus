#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDependencies
{
    // Intentionally no ArgumentNullException guards: reflection-pinned tests pass null by
    // position as a behavior anchor (ADR-0003 / issue #167). Direct assignment only.
    public HistoryPanelDependencies(
        IHistoryPanelRunState runState,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        IHistoryPanelServerHealthProbe? serverHealthProbe,
        BazaarDbLinkClient? accountLinkClient,
        Func<AccountLinkGate>? accountLinkGate
    )
    {
        RunState = runState;
        DataService = dataService;
        ReplayService = replayService;
        ServerHealthProbe = serverHealthProbe;
        AccountLinkClient = accountLinkClient;
        AccountLinkGate = accountLinkGate;
    }

    public IHistoryPanelRunState RunState { get; }

    public HistoryPanelDataService DataService { get; }

    public HistoryPanelReplayService ReplayService { get; }

    public IHistoryPanelServerHealthProbe? ServerHealthProbe { get; }

    public BazaarDbLinkClient? AccountLinkClient { get; }

    public Func<AccountLinkGate>? AccountLinkGate { get; }

    public BazaarDbAccountLinkStore AccountLinkStore { get; init; } = new();
}
