#nullable enable
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

public sealed class HistoryPanelDependencies
{
    public HistoryPanelDependencies(
        IRunContext runContext,
        HistoryPanelDataService dataService,
        IHistoryPanelReplay replayService,
        ModApiSession? modApiSession,
        BazaarDbLinkClient? accountLinkClient,
        Func<AccountLinkGate>? accountLinkGate,
        BazaarDbAccountLinkStore accountLinkStore,
        Func<float> unscaledTime,
        Func<string?> profileAccountId
    )
    {
        RunContext = runContext;
        DataService = dataService;
        ReplayService = replayService;
        ModApiSession = modApiSession;
        AccountLinkClient = accountLinkClient;
        AccountLinkGate = accountLinkGate;
        AccountLinkStore =
            accountLinkStore ?? throw new ArgumentNullException(nameof(accountLinkStore));
        UnscaledTime = unscaledTime ?? throw new ArgumentNullException(nameof(unscaledTime));
        ProfileAccountId =
            profileAccountId ?? throw new ArgumentNullException(nameof(profileAccountId));
    }

    public IRunContext RunContext { get; }

    public HistoryPanelDataService DataService { get; }

    public IHistoryPanelReplay ReplayService { get; }

    public ModApiSession? ModApiSession { get; }

    public BazaarDbLinkClient? AccountLinkClient { get; }

    public Func<AccountLinkGate>? AccountLinkGate { get; }

    public BazaarDbAccountLinkStore AccountLinkStore { get; }

    public Func<float> UnscaledTime { get; }

    public Func<string?> ProfileAccountId { get; }

    public HistoryFavoriteService? Favorites { get; init; }
}
