#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Upload;

/// <summary>
/// Pure activation policy shared by the Unity pump and unit tests. PTR stays a pump-side
/// precondition; feature enablement remains session behavior after activation.
/// </summary>
internal static class UploadPumpBootstrap
{
    internal static bool CanActivate(GameBuildChannel channel) => channel != GameBuildChannel.Ptr;

    internal static BundleUploadFeed.Session? ActivateIfAllowed(
        IBppServices services,
        UploadPumpCadence cadence
    )
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (!CanActivate(services.GameBuild.Channel))
        {
            // Session gate: no upload feed arms on the PTR build. The durable defense is the
            // build_channel row filter in the upload stores — it keeps PTR-recorded rows out of
            // uploads even after switching back to online.
            BppLog.DebugEvent(
                new BppLogEvent(BppLogFeatureScope.Upload, "upload.feed.skipped"),
                () => [("reason_code", UploadLogReasonCode.PtrBuild)]
            );
            return null;
        }

        return BundleUploadFeed.Activate(services, cadence);
    }
}
