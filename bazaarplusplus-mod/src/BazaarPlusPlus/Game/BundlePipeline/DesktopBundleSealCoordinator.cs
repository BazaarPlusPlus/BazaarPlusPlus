#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.BundlePipeline;

internal static class DesktopBundleSealCoordinator
{
    internal static BundleSealCoordinator Create(
        IBppServices services,
        Action<BundleSealStage, string>? faultProbe = null,
        IBundleScreenshotEncoder? screenshotEncoder = null,
        string? bundlerVersion = null,
        string? platform = null
    )
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        var dataRoot = services.Paths.RequireDataRoot();
        return new BundleSealCoordinator(
            services,
            new RunPayloadComposer(
                PathConstants.RunLogDatabase(dataRoot),
                PathConstants.CombatReplays(dataRoot),
                DesktopPvpBattleEvidence.Instance,
                SqliteOpenOptions.Default,
                bundlerVersion,
                platform
            ),
            screenshotEncoder ?? new BundleScreenshotEncoder(),
            faultProbe,
            SqliteOpenOptions.Default,
            bundlerVersion,
            platform
        );
    }
}
