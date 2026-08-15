#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Upload;

/// <summary>
/// Pure activation policy shared by the Unity pump and unit tests. PTR stays a pump-side
/// precondition; feature enablement remains session behavior after activation.
/// </summary>
internal static class UploadPumpBootstrap
{
    internal static bool CanActivate(GameBuildChannel channel) => channel != GameBuildChannel.Ptr;

    internal static IUploadFeedSession? ActivateIfAllowed(
        IBppServices services,
        IUploadFeed feed,
        UploadFeedLogState logState,
        UploadPumpCadence cadence
    )
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (feed == null)
            throw new ArgumentNullException(nameof(feed));
        if (logState == null)
            throw new ArgumentNullException(nameof(logState));

        var feedKind = feed.Kind;
        if (!CanActivate(services.GameBuild.Channel))
        {
            // Session gate: no upload feed arms on the PTR build. The durable defense is the
            // build_channel row filter in the upload stores — it keeps PTR-recorded rows out of
            // uploads even after switching back to online.
            BppLog.DebugEvent(
                UploadLogEvents.FeedSkipped,
                () =>
                    [
                        UploadLogEvents.FeedSkippedFeed.Bind(feedKind),
                        UploadLogEvents.FeedSkippedReasonCode.Bind(UploadLogReasonCode.PtrBuild),
                    ]
            );
            return null;
        }

        return feed.Activate(services, logState, cadence);
    }
}
