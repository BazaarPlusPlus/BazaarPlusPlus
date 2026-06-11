#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal interface IHistoryPanelServerHealthProbe
{
    Task<ModApiHealthProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

internal sealed class HistoryPanelServerHealthProbe : IHistoryPanelServerHealthProbe
{
    private readonly ModOnlineClient _onlineClient;

    public HistoryPanelServerHealthProbe(ModOnlineClient onlineClient)
    {
        _onlineClient = onlineClient ?? throw new ArgumentNullException(nameof(onlineClient));
    }

    public Task<ModApiHealthProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        return new ModApiHealthClient(_onlineClient.HttpClient, _onlineClient.Routes).ProbeAsync(
            cancellationToken
        );
    }
}

internal static class HistoryPanelServerHealthFormatter
{
    public static HistoryPanelServerHealthDisplayState Idle()
    {
        return new HistoryPanelServerHealthDisplayState(
            HistoryPanelText.CheckServerHealth(),
            true,
            null
        );
    }

    public static HistoryPanelServerHealthDisplayState Checking()
    {
        return new HistoryPanelServerHealthDisplayState(
            HistoryPanelText.CheckingServerHealth(),
            false,
            HistoryPanelText.CheckingServerConnectivity()
        );
    }

    public static HistoryPanelServerHealthDisplayState FromProbeResult(
        ModApiHealthProbeResult result
    )
    {
        if (result.Succeeded)
        {
            return new HistoryPanelServerHealthDisplayState(
                HistoryPanelText.CheckServerHealth(),
                true,
                HistoryPanelText.ServerHealthConnected(result.RoundTripMilliseconds)
            );
        }

        var error = string.IsNullOrWhiteSpace(result.Error)
            ? HistoryPanelText.Unknown()
            : result.Error!;
        return new HistoryPanelServerHealthDisplayState(
            HistoryPanelText.CheckServerHealth(),
            true,
            HistoryPanelText.ServerHealthFailed(result.RoundTripMilliseconds, error)
        );
    }

    public static HistoryPanelServerHealthDisplayState Unavailable()
    {
        return new HistoryPanelServerHealthDisplayState(
            HistoryPanelText.CheckServerHealth(),
            true,
            HistoryPanelText.ServerHealthUnavailable()
        );
    }
}

internal readonly struct HistoryPanelServerHealthDisplayState
{
    public HistoryPanelServerHealthDisplayState(
        string buttonText,
        bool buttonEnabled,
        string? statusMessage
    )
    {
        ButtonText = buttonText ?? throw new ArgumentNullException(nameof(buttonText));
        ButtonEnabled = buttonEnabled;
        StatusMessage = statusMessage;
    }

    public string ButtonText { get; }

    public bool ButtonEnabled { get; }

    public string? StatusMessage { get; }
}
