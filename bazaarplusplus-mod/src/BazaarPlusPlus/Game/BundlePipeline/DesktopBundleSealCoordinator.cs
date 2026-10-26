#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.BundlePipeline;

/// <summary>
/// Binds the desktop's game-typed PvP evidence reads and ImageSharp screenshot encoder into the
/// shared <see cref="BundleSealCoordinator"/>.
/// </summary>
internal static class DesktopBundleSealCoordinator
{
    // Tests inject failures at named seal stages and may wrap the encoder; production passes
    // neither.
    internal static BundleSealCoordinator Create(
        IBppServices services,
        Action<BundleSealStage, string>? faultProbe = null,
        IBundleScreenshotEncoder? screenshotEncoder = null
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
                SqliteOpenOptions.Default
            ),
            screenshotEncoder ?? new BundleScreenshotEncoder(),
            faultProbe,
            SqliteOpenOptions.Default
        );
    }
}
