#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDependencies
{
    public HistoryPanelDependencies(
        IRunContext runContext,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        ModApiSession? modApiSession,
        BazaarDbLinkClient? accountLinkClient,
        Func<AccountLinkGate>? accountLinkGate
    )
    {
        RunContext = runContext;
        DataService = dataService;
        ReplayService = replayService;
        ModApiSession = modApiSession;
        AccountLinkClient = accountLinkClient;
        AccountLinkGate = accountLinkGate;
    }

    public IRunContext RunContext { get; }

    public HistoryPanelDataService DataService { get; }

    public HistoryPanelReplayService ReplayService { get; }

    public ModApiSession? ModApiSession { get; }

    public BazaarDbLinkClient? AccountLinkClient { get; }

    public Func<AccountLinkGate>? AccountLinkGate { get; }

    public BazaarDbAccountLinkStore AccountLinkStore { get; init; } = new();
}
